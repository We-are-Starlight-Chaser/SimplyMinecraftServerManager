// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 反篡改检测器。
/// 通过多种手段检测扩展是否试图绕过沙箱或进行恶意操作。
///
/// 检测策略：
///   1. 进程模块监控（检测注入的 DLL）
///   2. 线程枚举（检测恶意线程创建）
///   3. 可疑 P/Invoke 调用检测
///   4. 文件系统越界访问检测
///   5. 网络连接监控
///   6. 操作系统 API 调用审计
/// </summary>
internal sealed class AntiTamper : IDisposable
{
    private readonly string _extensionId;
    private readonly string _extensionDataPath;
    private readonly string _extensionsDir;
    private readonly ILogger _logger;
    private readonly Timer _monitorTimer;
    private readonly Lock _lock = new();

    // 基线快照
    private HashSet<string> _baselineModules = [];
    private int _baselineThreadCount;

    // 追踪
    private readonly List<string> _violations = [];
    private bool _disposed;
    private readonly int _maxViolations;

    // 事件
    public event EventHandler<TamperEventArgs>? TamperDetected;

    // 已知安全模块前缀
    private static readonly HashSet<string> SafeModulePrefixes =
    [
        "System.", "Microsoft.", "mscorlib", "netstandard",
        "SimplyMinecraftServerManager", "WindowsBase",
        "PresentationCore", "PresentationFramework", "PresentationUI",
        "Accessibility", "UIAutomation",
    ];

    // 可疑 API 模式
    private static readonly HashSet<string> SuspiciousApis =
    [
        "VirtualAlloc", "VirtualProtect", "WriteProcessMemory",
        "CreateRemoteThread", "OpenProcess", "NtUnmapViewOfSection",
        "SetWindowsHookEx", "GetAsyncKeyState",
        "AdjustTokenPrivileges", "OpenThreadToken",
    ];

    private readonly int _monitorIntervalMs;

    public AntiTamper(
        string extensionId,
        string extensionDataPath,
        ILogger logger,
        int monitorIntervalMs = 5000,
        int maxViolations = 5)
    {
        _extensionId = extensionId;
        _extensionDataPath = extensionDataPath;
        _extensionsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions");
        _logger = logger;
        _maxViolations = maxViolations;
        _monitorIntervalMs = monitorIntervalMs;

        // 基线在 StartMonitoring() 中记录，而非构造时
        _monitorTimer = new Timer(
            callback: _ => Monitor(),
            state: null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    /// <summary>
    /// 启动监控。应在扩展 InitAsync 完成后调用，以确保基线准确。
    /// </summary>
    public void StartMonitoring()
    {
        RecordBaseline();
        _monitorTimer.Change(_monitorIntervalMs, _monitorIntervalMs);
    }

    /// <summary>
    /// 记录当前进程状态基线。
    /// 在扩展加载后调用。
    /// </summary>
    public void RecordBaseline()
    {
        lock (_lock)
        {
            try
            {
                var process = Process.GetCurrentProcess();

                _baselineModules = process.Modules
                    .Cast<ProcessModule>()
                    .Select(m => m.ModuleName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _baselineThreadCount = process.Threads.Count;

                _logger.Debug($"[{_extensionId}] 反篡改基线已记录 (模块={_baselineModules.Count}, 线程={_baselineThreadCount})");
            }
            catch (Exception ex)
            {
                _logger.Warn($"[{_extensionId}] 记录反篡改基线失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 验证路径是否在扩展数据目录内。
    /// 扩展访问文件时应调用此方法。
    /// </summary>
    public bool ValidatePath(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string extensionRoot = Path.GetFullPath(_extensionDataPath);

            if (fullPath.StartsWith(extensionRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 允许访问系统临时目录
            string tempPath = Path.GetFullPath(Path.GetTempPath());
            if (fullPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            OnTamper(TamperType.FileSystemViolation,
                $"扩展尝试访问数据目录外的路径: {fullPath}");

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查可疑的 P/Invoke 调用。
    /// </summary>
    public bool CheckPInvoke(string dllName, string? functionName = null)
    {
        // 可疑的系统 DLL
        string[] suspiciousDlls = ["kernel32.dll", "ntdll.dll", "advapi32.dll", "user32.dll"];

        if (suspiciousDlls.Any(d => string.Equals(d, dllName, StringComparison.OrdinalIgnoreCase)))
        {
            string func = functionName ?? "unknown";
            OnTamper(TamperType.SuspiciousPInvoke,
                $"检测到可疑 P/Invoke: {dllName}::{func}");
            return false;
        }

        return true;
    }

    private void Monitor()
    {
        if (_disposed) return;

        lock (_lock)
        {
            try
            {
                var process = Process.GetCurrentProcess();

                // 1. 检测新增模块（DLL 注入）
                CheckModuleInjection(process);

                // 2. 检测异常线程增长
                CheckThreadAnomaly(process);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{_extensionId}] AntiTamper monitor error: {ex.Message}");
            }
        }
    }

    private void CheckModuleInjection(Process process)
    {
        try
        {
            var modules = process.Modules;
            var currentModuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentModulePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < modules.Count; i++)
            {
                try
                {
                    var module = modules[i];
                    string name = module.ModuleName;
                    string path = module.FileName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        currentModuleNames.Add(name);
                        if (!string.IsNullOrEmpty(path))
                            currentModulePaths[name] = path;
                    }
                }
                catch { }
            }

            var newModules = currentModuleNames.Except(_baselineModules);

            foreach (string moduleName in newModules)
            {
                string? modulePath = currentModulePaths.GetValueOrDefault(moduleName);

                // 已知系统模块（SHA256 哈希匹配）→ 放行
                if (SystemIntegrityChecker.IsKnownSystemModule(moduleName, modulePath))
                {
                    continue;
                }

                // 扩展目录下的模块（其他扩展的 DLL）→ 放行
                if (!string.IsNullOrEmpty(modulePath) && IsPathInExtensionsDir(modulePath))
                {
                    continue;
                }

                // .NET 共享框架目录下的模块（运行时延迟加载）→ 放行
                if (!string.IsNullOrEmpty(modulePath) && IsPathInDotNetSharedFrameworks(modulePath))
                {
                    continue;
                }

                OnTamper(TamperType.ModuleInjection,
                    $"检测到新模块加载: {moduleName}");
            }
        }
        catch
        {
            // 进程模块枚举可能在某些状态下失败
        }
    }

    private bool IsPathInExtensionsDir(string modulePath)
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
        catch
        {
            return false;
        }
    }

    private void CheckThreadAnomaly(Process process)
    {
        try
        {
            int currentThreadCount = process.Threads.Count;
            int threadDelta = currentThreadCount - _baselineThreadCount;

            // 线程数增长超过 100% 或绝对值超过 50
            if (threadDelta > Math.Max(_baselineThreadCount, 50))
            {
                OnTamper(TamperType.ThreadAnomaly,
                    $"线程数异常增长: {currentThreadCount} (基线={_baselineThreadCount}, 增长={threadDelta})");
            }
        }
        catch
        {
            // 线程枚举可能在某些状态下失败
        }
    }

    private void OnTamper(TamperType type, string detail)
    {
        if (_violations.Count >= _maxViolations) return;

        _violations.Add($"[{DateTime.UtcNow:HH:mm:ss}] {type}: {detail}");

        bool isTerminal = _violations.Count >= _maxViolations;

        _logger.Error($"[{_extensionId}] 反篡改检测: {type} - {detail}" +
                      (isTerminal ? " (已达上限)" : ""));

        TamperDetected?.Invoke(this, new TamperEventArgs
        {
            ExtensionId = _extensionId,
            Type = type,
            Detail = detail,
            ViolationNumber = _violations.Count,
            IsTerminal = isTerminal
        });
    }

    /// <summary>获取所有违规记录</summary>
    public IReadOnlyList<string> GetViolations()
    {
        lock (_lock) return _violations.AsReadOnly();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitorTimer.Dispose();
    }

    public enum TamperType
    {
        ModuleInjection,
        ThreadAnomaly,
        SuspiciousPInvoke,
        FileSystemViolation,
        NetworkViolation,
        CodeInjection
    }

    public sealed class TamperEventArgs : EventArgs
    {
        public required string ExtensionId { get; init; }
        public required TamperType Type { get; init; }
        public required string Detail { get; init; }
        public required int ViolationNumber { get; init; }
        public required bool IsTerminal { get; init; }
    }
}
