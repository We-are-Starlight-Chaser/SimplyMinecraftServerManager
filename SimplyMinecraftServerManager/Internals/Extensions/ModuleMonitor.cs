// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 实时模块加载监控器。
/// 使用高频后台线程监控进程模块变化，检测 DLL 注入和可疑模块加载。
///
/// 监控策略：
///   1. 高频采样（100ms 间隔）检测模块变化
///   2. 基线快照对比
///   3. 危险模块黑名单
///   4. 模块来源验证（路径校验）
///   5. 异步事件通知
///   6. 模块加载频率异常检测
/// </summary>
internal sealed class ModuleMonitor : IDisposable
{
    private readonly string _extensionId;
    private readonly ILogger _logger;
    private readonly Thread _monitorThread;
    private readonly ManualResetEventSlim _stopEvent = new();
    private readonly ConcurrentBag<ModuleLogEntry> _auditLog = [];
    private readonly ConcurrentDictionary<string, int> _moduleLoadFrequency = new();

    // 基线
    private HashSet<string> _baselineModuleNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _baselineModulePaths = new(StringComparer.OrdinalIgnoreCase);
    private int _baselineModuleCount;

    // 追踪
    private readonly HashSet<string> _observedModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private bool _disposed;
    private int _violationCount;

    // 配置
    private readonly int _maxModuleLoadPerSecond;
    private readonly int _monitorIntervalMs;
    private readonly int _maxViolations;

    // 事件
    public event EventHandler<ModuleEventArgs>? ModuleLoaded;
    public event EventHandler<ModuleEventArgs>? ModuleViolation;

    // 危险模块黑名单（硬编码，不可覆盖）
    private static readonly HashSet<string> DangerousModules = new(StringComparer.OrdinalIgnoreCase)
    {
        // 远程线程注入常用
        "dbghelp.dll", "dbgcore.dll",
        // 代码注入
        "clr.dll", "mscorwks.dll", "mscorjit.dll",
        // 键盘记录
        "user32.dll", "gdi32.dll",
        // 网络
        "ws2_32.dll", "wininet.dll", "urlmon.dll",
        // 进程/线程操作
        "kernel32.dll", "ntdll.dll", "advapi32.dll",
        // COM
        "ole32.dll", "oleaut32.dll", "combase.dll",
        // WMI
        "wbemcomn.dll", "wbemdisp.dll",
    };

    // 扩展目录路径（启动时确定）
    private readonly string _extensionsDir;

    static ModuleMonitor()
    {
    }

    public IReadOnlyCollection<ModuleLogEntry> AuditLog => [.. _auditLog];
    public int ViolationCount => Interlocked.CompareExchange(ref _violationCount, 0, 0);

    public ModuleMonitor(
        string extensionId,
        ILogger logger,
        int monitorIntervalMs = 100,
        int maxModuleLoadPerSecond = 5,
        int maxViolations = 5)
    {
        _extensionId = extensionId;
        _logger = logger;
        _monitorIntervalMs = monitorIntervalMs;
        _maxModuleLoadPerSecond = maxModuleLoadPerSecond;
        _maxViolations = maxViolations;
        _extensionsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions");

        // 记录基线
        RecordBaseline();

        // 启动监控线程
        _monitorThread = new Thread(MonitorLoop)
        {
            IsBackground = true,
            Name = $"ModuleMonitor_{extensionId}",
            Priority = ThreadPriority.BelowNormal,
        };
        _monitorThread.Start();
    }

    /// <summary>
    /// 记录当前进程模块基线。
    /// 在扩展加载后调用。
    /// </summary>
    public void RecordBaseline()
    {
        lock (_lock)
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var modules = process.Modules;

                _baselineModuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _baselineModulePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < modules.Count; i++)
                {
                    try
                    {
                        var module = modules[i];
                        string name = module.ModuleName;
                        string path = module.FileName;

                        if (!string.IsNullOrEmpty(name))
                        {
                            _baselineModuleNames.Add(name);
                            if (!string.IsNullOrEmpty(path))
                            {
                                _baselineModulePaths[name] = path;
                            }
                        }
                    }
                    catch
                    {
                        // 某些模块可能无法访问
                    }
                }

                _baselineModuleCount = _baselineModuleNames.Count;

                // 初始化已观察模块集合
                _observedModules.Clear();
                foreach (string name in _baselineModuleNames)
                {
                    _observedModules.Add(name);
                }

                _logger.Debug($"[{_extensionId}] 模块监控基线已记录 ({_baselineModuleCount} 个模块)");
            }
            catch (Exception ex)
            {
                _logger.Warn($"[{_extensionId}] 记录模块监控基线失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 验证模块是否允许加载。
    /// 返回 true 表示允许，false 表示应阻止。
    ///
    /// 校验流程：
    ///   1. 危险模块黑名单 → 拦截
    ///   2. 已知系统模块（SHA256 哈希匹配）→ 放行
    ///   3. 加载频率异常 → 拦截
    ///   4. 扩展目录下的模块 → 放行
    ///   5. .NET 共享框架目录下的模块 → 放行（哈希表未覆盖的运行时模块）
    ///   6. 其他 → 拦截
    /// </summary>
    public bool ValidateModuleLoad(string moduleName, string? modulePath = null)
    {
        if (_disposed) return false;

        // 1. 检查危险模块黑名单
        if (DangerousModules.Contains(moduleName))
        {
            LogViolation(moduleName, modulePath, "危险模块黑名单");
            return false;
        }

        // 2. 已知系统模块：通过 SHA256 哈希比对确认是官方原始版本 → 放行
        if (SystemIntegrityChecker.IsKnownSystemModule(moduleName, modulePath))
        {
            LogAccess(moduleName, modulePath);
            return true;
        }

        // 3. 检查模块加载频率
        if (!CheckLoadFrequency(moduleName))
        {
            LogViolation(moduleName, modulePath, "模块加载频率超限");
            return false;
        }

        // 4. 扩展目录下的模块 → 放行
        if (!string.IsNullOrEmpty(modulePath) && IsModulePathInExtensionsDir(modulePath))
        {
            LogAccess(moduleName, modulePath);
            return true;
        }

        // 5. .NET 共享框架目录下的模块 → 放行（哈希表未覆盖的运行时模块，如 Serialization.Formatters 等）
        if (!string.IsNullOrEmpty(modulePath) && IsPathInDotNetSharedFrameworks(modulePath))
        {
            LogAccess(moduleName, modulePath);
            return true;
        }

        // 6. 其他未知模块 → 拦截
        LogViolation(moduleName, modulePath, "未知模块，不在已知安全位置");
        return false;
    }

    /// <summary>
    /// 检查指定模块是否在基线中（已知安全）。
    /// </summary>
    public bool IsBaselineModule(string moduleName)
    {
        lock (_lock)
        {
            return _baselineModuleNames.Contains(moduleName);
        }
    }

    /// <summary>
    /// 获取基线模块数量。
    /// </summary>
    public int BaselineModuleCount
    {
        get { lock (_lock) return _baselineModuleCount; }
    }

    private void MonitorLoop()
    {
        while (!_stopEvent.Wait(_monitorIntervalMs))
        {
            if (_disposed) break;

            try
            {
                CheckForNewModules();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{_extensionId}] ModuleMonitor error: {ex.Message}");
            }
        }
    }

    private void CheckForNewModules()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var currentModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var modules = process.Modules;
            for (int i = 0; i < modules.Count; i++)
            {
                try
                {
                    var module = modules[i];
                    string name = module.ModuleName;
                    string path = module.FileName;

                    if (!string.IsNullOrEmpty(name))
                    {
                        currentModules.Add(name);
                        if (!string.IsNullOrEmpty(path))
                        {
                            currentPaths[name] = path;
                        }
                    }
                }
                catch
                {
                    // 某些模块可能无法访问
                }
            }

            lock (_lock)
            {
                // 检测新增模块
                var newModules = currentModules.Except(_baselineModuleNames).Except(_observedModules);

                foreach (string moduleName in newModules)
                {
                    string? modulePath = currentPaths.GetValueOrDefault(moduleName);

                    // 触发模块加载事件
                    ModuleLoaded?.Invoke(this, new ModuleEventArgs
                    {
                        ExtensionId = _extensionId,
                        ModuleName = moduleName,
                        ModulePath = modulePath,
                        IsBaselineModule = false,
                    });

                    // 验证模块
                    if (!ValidateModuleLoad(moduleName, modulePath))
                    {
                        // 违规已记录
                    }

                    _observedModules.Add(moduleName);
                }

                // 检测已移除的模块（可能被卸载或替换）
                var removedModules = _baselineModuleNames.Intersect(_observedModules).Except(currentModules);
                foreach (string moduleName in removedModules)
                {
                    _logger.Debug($"[{_extensionId}] 模块已卸载: {moduleName}");
                }
            }
        }
        catch
        {
            // 进程模块枚举可能在某些状态下失败
        }
    }

    private bool CheckLoadFrequency(string moduleName)
    {
        lock (_lock)
        {
            int count = _moduleLoadFrequency.AddOrUpdate(moduleName, 1, (_, v) => v + 1);

            // 每秒重置计数器
            if (count > _maxModuleLoadPerSecond)
            {
                _logger.Warn($"[{_extensionId}] 模块 '{moduleName}' 加载频率超限: {count} > {_maxModuleLoadPerSecond}");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 检查模块是否位于扩展目录下（扩展之间互相加载的 DLL）。
    /// </summary>
    private bool IsModulePathInExtensionsDir(string modulePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(modulePath);
            return fullPath.StartsWith(_extensionsDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查模块是否位于 .NET 共享框架目录下（Microsoft.NETCore.App, Microsoft.WindowsDesktop.App 等）。
    /// 用于放行哈希表未覆盖的运行时延迟加载模块。
    /// </summary>
    private static bool IsPathInDotNetSharedFrameworks(string modulePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(modulePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(dir)) return false;

            // 共享框架位于 shared/Microsoft.{FrameworkName}/{version}/ 下
            // 向上两级到达 shared/ 目录
            string? versionDir = Path.GetDirectoryName(dir);
            string? frameworkDir = versionDir is not null ? Path.GetDirectoryName(versionDir) : null;
            string? sharedRoot = frameworkDir is not null ? Path.GetDirectoryName(frameworkDir) : null;

            if (string.IsNullOrEmpty(sharedRoot)) return false;

            // 检查是否在 shared/ 下的某个 Microsoft.* 框架目录中
            return fullPath.StartsWith(sharedRoot, StringComparison.OrdinalIgnoreCase)
                && fullPath.Contains("Microsoft.", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void LogViolation(string moduleName, string? modulePath, string reason)
    {
        int violationNum = Interlocked.Increment(ref _violationCount);

        _logger.Warn($"[{_extensionId}] 模块违规 #{violationNum}: {moduleName} ({modulePath}) - {reason}");
        _auditLog.Add(new ModuleLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            ModuleName = moduleName,
            ModulePath = modulePath,
            Allowed = false,
            DenyReason = reason,
            ViolationNumber = violationNum,
        });

        bool isTerminal = violationNum >= _maxViolations;
        ModuleViolation?.Invoke(this, new ModuleEventArgs
        {
            ExtensionId = _extensionId,
            ModuleName = moduleName,
            ModulePath = modulePath,
            IsBaselineModule = false,
            IsTerminal = isTerminal,
            ViolationNumber = violationNum,
        });
    }

    private void LogAccess(string moduleName, string? modulePath)
    {
        _auditLog.Add(new ModuleLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            ModuleName = moduleName,
            ModulePath = modulePath,
            Allowed = true,
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopEvent.Set();
        _monitorThread.Join(1000);
        _stopEvent.Dispose();
    }

    /// <summary>模块监控日志条目</summary>
    public sealed class ModuleLogEntry
    {
        public DateTime Timestamp { get; init; }
        public required string ExtensionId { get; init; }
        public required string ModuleName { get; init; }
        public string? ModulePath { get; init; }
        public bool Allowed { get; init; }
        public string? DenyReason { get; init; }
        public int ViolationNumber { get; init; }
    }

    /// <summary>模块事件参数</summary>
    public sealed class ModuleEventArgs : EventArgs
    {
        public required string ExtensionId { get; init; }
        public required string ModuleName { get; init; }
        public string? ModulePath { get; init; }
        public bool IsBaselineModule { get; init; }
        public bool IsTerminal { get; init; }
        public int ViolationNumber { get; init; }
    }
}
