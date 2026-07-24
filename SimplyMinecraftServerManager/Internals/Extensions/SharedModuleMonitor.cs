// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 共享模块监控器：单线程监控所有扩展的模块加载行为。
/// 替代每个扩展独立线程的方案，大幅降低 CPU 开销。
///
/// 监控策略：
///   1. 每 5 秒采样一次进程模块（而非 100ms × N 个线程）
///   2. 单线程遍历一次模块列表，同时检查所有已注册的扩展
///   3. 各扩展保持独立的违规计数和事件
/// </summary>
internal sealed class SharedModuleMonitor : IDisposable
{
    private readonly Thread _monitorThread;
    private readonly ManualResetEventSlim _stopEvent = new();
    private readonly ConcurrentDictionary<string, ModuleMonitorEntry> _entries = new();
    private readonly ILogger _logger;
    private bool _disposed;

    private const int DefaultMonitorIntervalMs = 5000; // 5 秒（原 100ms × N 个线程）

    // 基线模块快照（所有扩展共享）
    private HashSet<string> _baselineModuleNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _baselineModulePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _baselineLock = new();
    private bool _baselineRecorded;

    // 已观察模块集合
    private readonly HashSet<string> _observedModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _observedLock = new();

    // 扩展目录路径
    private readonly string _extensionsDir;

    public SharedModuleMonitor(ILogger logger)
    {
        _logger = logger;
        _extensionsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions");

        _monitorThread = new Thread(MonitorLoop)
        {
            IsBackground = true,
            Name = "SharedModuleMonitor",
            Priority = ThreadPriority.BelowNormal,
        };
        _monitorThread.Start();
    }

    /// <summary>
    /// 注册一个扩展到共享监控器。
    /// 返回对应的条目对象，扩展的 ModuleMonitor 可通过此对象获取违规信息。
    /// </summary>
    public ModuleMonitorEntry Register(string extensionId, int maxModuleLoadPerSecond = 5, int maxViolations = 5)
    {
        var entry = new ModuleMonitorEntry(extensionId, maxModuleLoadPerSecond, maxViolations);
        _entries[extensionId] = entry;

        // 首次注册时记录基线
        lock (_baselineLock)
        {
            if (!_baselineRecorded)
            {
                RecordBaselineInternal();
            }
        }

        _logger.Debug($"[SharedModuleMonitor] 扩展 '{extensionId}' 已注册 (当前注册数: {_entries.Count})");
        return entry;
    }

    /// <summary>
    /// 注销一个扩展。
    /// </summary>
    public void Unregister(string extensionId)
    {
        _entries.TryRemove(extensionId, out _);
        _logger.Debug($"[SharedModuleMonitor] 扩展 '{extensionId}' 已注销 (当前注册数: {_entries.Count})");
    }

    /// <summary>
    /// 记录当前进程模块基线。
    /// </summary>
    private void RecordBaselineInternal()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var modules = process.Modules;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < modules.Count; i++)
            {
                try
                {
                    var module = modules[i];
                    string name = module.ModuleName;
                    string path = module.FileName;

                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                        if (!string.IsNullOrEmpty(path))
                        {
                            paths[name] = path;
                        }
                    }
                }
                catch
                {
                    // 某些模块可能无法访问
                }
            }

            _baselineModuleNames = names;
            _baselineModulePaths = paths;
            _baselineRecorded = true;

            lock (_observedLock)
            {
                _observedModules.Clear();
                foreach (string name in _baselineModuleNames)
                {
                    _observedModules.Add(name);
                }
            }

            _logger.Debug($"[SharedModuleMonitor] 基线已记录 ({_baselineModuleNames.Count} 个模块)");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[SharedModuleMonitor] 记录基线失败: {ex.Message}");
        }
    }

    private void MonitorLoop()
    {
        while (!_stopEvent.Wait(DefaultMonitorIntervalMs))
        {
            if (_disposed) break;

            try
            {
                CheckForNewModules();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SharedModuleMonitor] 错误: {ex.Message}");
            }
        }
    }

    private void CheckForNewModules()
    {
        if (_entries.IsEmpty) return;

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

            List<string> newModules;
            lock (_observedLock)
            {
                newModules = [..currentModules.Except(_baselineModuleNames).Except(_observedModules)];
                foreach (string moduleName in newModules)
                {
                    _observedModules.Add(moduleName);
                }
            }

            if (newModules.Count == 0) return;

            // 对每个新模块，检查所有已注册的扩展
            foreach (string moduleName in newModules)
            {
                string? modulePath = currentPaths.GetValueOrDefault(moduleName);

                foreach (var kvp in _entries)
                {
                    var entry = kvp.Value;
                    entry.ValidateModule(moduleName, modulePath, _extensionsDir);
                }
            }
        }
        catch
        {
            // 进程模块枚举可能在某些状态下失败
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopEvent.Set();
        _monitorThread.Join(2000);
        _stopEvent.Dispose();
    }

    /// <summary>
    /// 单个扩展的模块监控条目
    /// </summary>
    internal sealed class ModuleMonitorEntry(string extensionId, int maxModuleLoadPerSecond, int maxViolations) : IDisposable
    {
        private readonly string _extensionId = extensionId;
        private readonly int _maxModuleLoadPerSecond = maxModuleLoadPerSecond;
        private readonly int _maxViolations = maxViolations;
        private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _moduleLoadFrequency = new();
        private int _violationCount;
        private int _disposed;

        private readonly ModuleLogEntry[] _auditRing = new ModuleLogEntry[512];
        private int _auditHead;
        private int _auditCount;
        private readonly Lock _auditLock = new();

        public event EventHandler<ModuleEventArgs>? ModuleLoaded;
        public event EventHandler<ModuleEventArgs>? ModuleViolation;

        public IReadOnlyCollection<ModuleLogEntry> AuditLog
        {
            get
            {
                lock (_auditLock)
                {
                    if (_auditCount == 0) return [];
                    var result = new List<ModuleLogEntry>(_auditCount);
                    int start = (_auditHead - _auditCount + 512) % 512;
                    for (int i = 0; i < _auditCount; i++)
                        result.Add(_auditRing[(start + i) % 512]);
                    return result;
                }
            }
        }

        public int ViolationCount => Volatile.Read(ref _violationCount);

        // 危险模块黑名单（硬编码，不可覆盖）
        private static readonly HashSet<string> DangerousModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "dbghelp.dll", "dbgcore.dll",
        "clr.dll", "mscorwks.dll", "mscorjit.dll",
        "user32.dll", "gdi32.dll",
        "ws2_32.dll", "wininet.dll", "urlmon.dll",
        "kernel32.dll", "ntdll.dll", "advapi32.dll",
        "ole32.dll", "oleaut32.dll", "combase.dll",
        "wbemcomn.dll", "wbemdisp.dll",
    };

        public void ValidateModule(string moduleName, string? modulePath, string extensionsDir)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            // 1. 检查危险模块黑名单
            if (DangerousModules.Contains(moduleName))
            {
                LogViolation(moduleName, modulePath, "危险模块黑名单");
                return;
            }

            // 2. 已知系统模块 → 放行
            if (SystemIntegrityChecker.IsKnownSystemModule(moduleName, modulePath))
            {
                LogAccess(moduleName, modulePath);
                return;
            }

            // 3. 检查模块加载频率
            if (!CheckLoadFrequency(moduleName))
            {
                LogViolation(moduleName, modulePath, "模块加载频率超限");
                return;
            }

            // 4. 扩展目录下的模块 → 放行
            if (!string.IsNullOrEmpty(modulePath) && IsModulePathInExtensionsDir(modulePath, extensionsDir))
            {
                LogAccess(moduleName, modulePath);
                return;
            }

            // 5. .NET 共享框架目录下的模块 → 放行
            if (!string.IsNullOrEmpty(modulePath) && IsPathInDotNetSharedFrameworks(modulePath))
            {
                LogAccess(moduleName, modulePath);
                return;
            }

            // 6. 其他未知模块 → 拦截
            LogViolation(moduleName, modulePath, "未知模块，不在已知安全位置");
        }

        private bool CheckLoadFrequency(string moduleName)
        {
            var now = DateTime.UtcNow;
            var (Count, WindowStart) = _moduleLoadFrequency.AddOrUpdate(
                moduleName,
                _ => (1, now),
                (_, existing) =>
                {
                    if ((now - existing.WindowStart).TotalSeconds >= 1)
                        return (1, now);
                    return (existing.Count + 1, existing.WindowStart);
                });

            return Count <= _maxModuleLoadPerSecond;
        }

        private static bool IsModulePathInExtensionsDir(string modulePath, string extensionsDir)
        {
            try
            {
                string fullPath = Path.GetFullPath(modulePath);
                return fullPath.StartsWith(extensionsDir, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsPathInDotNetSharedFrameworks(string modulePath)
        {
            try
            {
                string fullPath = Path.GetFullPath(modulePath);
                string? dir = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(dir)) return false;

                string? versionDir = Path.GetDirectoryName(dir);
                string? frameworkDir = versionDir is not null ? Path.GetDirectoryName(versionDir) : null;
                string? sharedRoot = frameworkDir is not null ? Path.GetDirectoryName(frameworkDir) : null;

                if (string.IsNullOrEmpty(sharedRoot)) return false;

                return fullPath.StartsWith(sharedRoot, StringComparison.OrdinalIgnoreCase)
                    && fullPath.Contains("Microsoft.", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ===== 环形缓冲写入 =====
        private void AddAuditEntry(ModuleLogEntry entry)
        {
            lock (_auditLock)
            {
                _auditRing[_auditHead] = entry;
                _auditHead = (_auditHead + 1) % 512;
                if (_auditCount < 512) _auditCount++;
            }
        }

        private void LogViolation(string moduleName, string? modulePath, string reason)
        {
            int violationNum = Interlocked.Increment(ref _violationCount);

            AddAuditEntry(new ModuleLogEntry
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
            AddAuditEntry(new ModuleLogEntry
            {
                Timestamp = DateTime.UtcNow,
                ExtensionId = _extensionId,
                ModuleName = moduleName,
                ModulePath = modulePath,
                Allowed = true,
            });
        }

        // ===== Dispose =====
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // 1. 清除事件委托链：断开外部订阅者引用，允许 GC 回收
            ModuleLoaded = null;
            ModuleViolation = null;

            // 2. 清空频率追踪字典
            _moduleLoadFrequency.Clear();

            // 3. 清空审计日志环（释放字符串引用）
            lock (_auditLock)
            {
                Array.Clear(_auditRing, 0, _auditRing.Length);
                _auditHead = 0;
                _auditCount = 0;
            }
        }
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
