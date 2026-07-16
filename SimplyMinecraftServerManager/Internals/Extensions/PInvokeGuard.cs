// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// P/Invoke 守卫：监控和阻止扩展使用危险的非托管 API 调用。
///
/// 防护策略：
///   1. 危险 Win32 API 函数黑名单
///   2. 危险非托管库加载检测
///   3. P/Invoke 调用频率监控
///   4. 内存操作 API 监控
///   5. 进程/线程操作 API 监控
/// </summary>
internal sealed class PInvokeGuard : IDisposable
{
    private readonly string _extensionId;
    private readonly ILogger _logger;
    private readonly ConcurrentBag<PInvokeLogEntry> _auditLog = [];
    private readonly ConcurrentDictionary<string, int> _callFrequency = new();
    private readonly Timer _frequencyResetTimer;
    private readonly Lock _lock = new();
    private bool _disposed;

    // 配置
    private readonly int _maxCallsPerMinute;
    private readonly bool _blockDangerousApis;

    // 危险 Win32 API 函数（硬编码，不可覆盖）
    private static readonly HashSet<string> DangerousWin32Apis = new(StringComparer.OrdinalIgnoreCase)
    {
        // 进程操作
        "CreateProcess", "CreateProcessAsUser", "CreateProcessWithLogonW",
        "CreateProcessWithTokenW", "OpenProcess", "TerminateProcess",
        "ShellExecute", "ShellExecuteEx", "CreateThread", "CreateRemoteThread",
        "CreateRemoteThreadEx",

        // 内存操作
        "VirtualAlloc", "VirtualAllocEx", "VirtualProtect", "VirtualProtectEx",
        "WriteProcessMemory", "ReadProcessMemory", "NtWriteVirtualMemory",
        "NtReadVirtualMemory", "RtlMoveMemory", "RtlCopyMemory",

        // 注册表操作
        "RegCreateKeyEx", "RegSetValueEx", "RegDeleteKey", "RegDeleteValue",
        "RegOpenKeyEx", "RegQueryValueEx",

        // 文件操作
        "CreateFile", "DeleteFile", "MoveFile", "CopyFile",
        "CreateDirectory", "RemoveDirectory",

        // 网络操作
        "InternetOpen", "InternetConnect", "HttpOpenRequest", "HttpSendRequest",
        "URLDownloadToFile", "InternetReadFile",

        // 钩子操作
        "SetWindowsHookEx", "UnhookWindowsHookEx", "CallNextHookEx",
        "SetWinEventHook",

        // 注入操作
        "LoadLibrary", "LoadLibraryEx", "GetProcAddress", "FreeLibrary",
        "GetModuleHandle", "GetModuleHandleEx",

        // 系统信息
        "GetSystemDirectory", "GetWindowsDirectory", "GetTempPath",
        "GetEnvironmentVariable", "SetEnvironmentVariable",

        // 用户/权限操作
        "AdjustTokenPrivileges", "OpenProcessToken", "GetTokenInformation",
        "CreateWellKnownSid", "ConvertSidToStringSid",

        // 异常处理
        "AddVectoredExceptionHandler", "RemoveVectoredExceptionHandler",
        "SetUnhandledExceptionFilter",
    };

    // 危险非托管库（硬编码，不可覆盖）
    private static readonly HashSet<string> DangerousNativeLibraries = new(StringComparer.OrdinalIgnoreCase)
    {
        "kernel32.dll", "ntdll.dll", "advapi32.dll", "user32.dll",
        "gdi32.dll", "shell32.dll", "ole32.dll", "oleaut32.dll",
        "msvcrt.dll", "msvcr100.dll", "msvcr110.dll", "msvcr120.dll",
        "crypt32.dll", "wininet.dll", "ws2_32.dll", "wldap32.dll",
        "netapi32.dll", "secur32.dll", "schannel.dll",
    };

    // 允许的非托管库（扩展通常需要的）
    private static readonly HashSet<string> AllowedNativeLibraries = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqlite3.dll", "libssl.dll", "libcrypto.dll",
        "zlib1.dll", "libz.dll",
    };

    public IReadOnlyCollection<PInvokeLogEntry> AuditLog => [.. _auditLog];

    public PInvokeGuard(
        string extensionId,
        ILogger logger,
        int maxCallsPerMinute = 100,
        bool blockDangerousApis = true)
    {
        _extensionId = extensionId;
        _logger = logger;
        _maxCallsPerMinute = maxCallsPerMinute;
        _blockDangerousApis = blockDangerousApis;

        _frequencyResetTimer = new Timer(
            callback: _ => ResetFrequency(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// 验证 P/Invoke 调用是否允许。
    /// </summary>
    /// <param name="libraryName">非托管库名称</param>
    /// <param name="functionName">函数名称</param>
    /// <returns>是否允许调用</returns>
    public bool ValidatePInvokeCall(string libraryName, string functionName)
    {
        if (_disposed) return false;

        // 1. 检查调用频率
        if (!CheckFrequency())
        {
            LogDenied(libraryName, functionName, "P/Invoke 调用频率超限");
            return false;
        }

        // 2. 检查库是否在危险列表中
        if (_blockDangerousApis && IsDangerousLibrary(libraryName))
        {
            LogDenied(libraryName, functionName, $"危险非托管库: {libraryName}");
            return false;
        }

        // 3. 检查函数是否在危险列表中
        if (_blockDangerousApis && IsDangerousFunction(functionName))
        {
            LogDenied(libraryName, functionName, $"危险 Win32 API: {functionName}");
            return false;
        }

        // 4. 记录审计日志
        LogAccess(libraryName, functionName);

        return true;
    }

    /// <summary>
    /// 验证 DllImport 属性的库名称。
    /// </summary>
    public bool ValidateDllImport(string libraryName)
    {
        if (_disposed) return false;

        // 检查库是否允许
        if (_blockDangerousApis && IsDangerousLibrary(libraryName))
        {
            LogDenied(libraryName, "DllImport", $"危险非托管库: {libraryName}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 检查扩展是否可以使用 P/Invoke。
    /// </summary>
    public bool CanUsePInvoke()
    {
        return !_disposed;
    }

    private static bool IsDangerousLibrary(string libraryName)
    {
        string libName = Path.GetFileName(libraryName).ToLowerInvariant();

        // 允许列表优先
        if (AllowedNativeLibraries.Contains(libName))
        {
            return false;
        }

        return DangerousNativeLibraries.Contains(libName);
    }

    private static bool IsDangerousFunction(string functionName)
    {
        return DangerousWin32Apis.Contains(functionName);
    }

    private bool CheckFrequency()
    {
        lock (_lock)
        {
            string key = _extensionId;
            int count = _callFrequency.AddOrUpdate(key, 1, (_, v) => v + 1);

            if (count > _maxCallsPerMinute)
            {
                _logger.Warn($"[{_extensionId}] P/Invoke 调用频率超限: {count} > {_maxCallsPerMinute}");
                return false;
            }

            return true;
        }
    }

    private void ResetFrequency()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _callFrequency.Clear();
        }
    }

    private void LogDenied(string libraryName, string functionName, string reason)
    {
        _logger.Warn($"[{_extensionId}] P/Invoke 调用拒绝: Library={libraryName}, Function={functionName}, Reason={reason}");
        _auditLog.Add(new PInvokeLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            LibraryName = libraryName,
            FunctionName = functionName,
            Allowed = false,
            DenyReason = reason
        });
    }

    private void LogAccess(string libraryName, string functionName)
    {
        _auditLog.Add(new PInvokeLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            LibraryName = libraryName,
            FunctionName = functionName,
            Allowed = true
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frequencyResetTimer.Dispose();
    }

    /// <summary>P/Invoke 调用日志条目</summary>
    public sealed class PInvokeLogEntry
    {
        public DateTime Timestamp { get; init; }
        public required string ExtensionId { get; init; }
        public required string LibraryName { get; init; }
        public required string FunctionName { get; init; }
        public bool Allowed { get; init; }
        public string? DenyReason { get; init; }
    }
}
