// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 进程执行守卫：监控和阻止扩展执行任意可执行文件。
///
/// 防护策略：
///   1. 危险可执行文件扩展名过滤
///   2. 危险系统工具黑名单
///   3. 进程创建监控和审计
///   4. 异常进程行为检测
///   5. 进程执行频率限制
/// </summary>
internal sealed partial class ProcessGuard : IDisposable
{
    private readonly string _extensionId;
    private readonly ILogger _logger;
    private readonly ConcurrentBag<ProcessLogEntry> _auditLog = [];
    private readonly ConcurrentDictionary<string, int> _processFrequency = new();
    private readonly Timer _frequencyResetTimer;
    private readonly Lock _lock = new();
    private bool _disposed;

    // 配置
    private readonly int _maxProcessStartsPerMinute;
    private readonly bool _blockAllExecutables;

    // 危险可执行文件扩展名（硬编码，不可覆盖）
    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".msi", ".msp", ".mst", ".pif",
        ".bat", ".cmd", ".com", ".cpl", ".hta", ".inf", ".ins",
        ".isp", ".jse", ".lnk", ".msc", ".msi", ".msp", ".mst",
        ".pif", ".ps1", ".ps2", ".psm1", ".psc1", ".psc2",
        ".reg", ".rgs", ".scf", ".scm", ".sct", ".shb", ".shs",
        ".vbe", ".vbs", ".vbscript", ".ws", ".wsc", ".wsf", ".wsh",
        ".xbap", ".xnk"
    };

    // 危险系统工具（硬编码，不可覆盖）
    private static readonly HashSet<string> DangerousSystemTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe",
        "mshta.exe", "regsvr32.exe", "regsvr64.exe", "rundll32.exe",
        "certutil.exe", "bitsadmin.exe", "msiexec.exe", "msbuild.exe",
        "installutil.exe", "regasm.exe", "regsvcs.exe", "csc.exe",
        "vbc.exe", "fsmgmt.msc", "mmc.exe", "eventvwr.exe",
        "services.exe", "taskmgr.exe", "taskkill.exe", "tasklist.exe",
        "net.exe", "net1.exe", "netsh.exe", "nltest.exe",
        "nbtstat.exe", "nslookup.exe", "ping.exe", "tracert.exe",
        "pathping.exe", "ftp.exe", "telnet.exe", "ssh.exe",
        "scp.exe", "sftp.exe", "curl.exe", "wget.exe",
        "bitsadmin.exe", "msdeploy.exe", "appcmd.exe",
        "format.com", "attrib.exe", "cacls.exe", "icacls.exe",
        "takeown.exe", "xcacls.exe", "cipher.exe", "compact.exe",
        "defrag.exe", "diskpart.exe", "diskperf.exe", "driverquery.exe",
        "fc.exe", "find.exe", "findstr.exe", "forfiles.exe",
        "fsutil.exe", "getmac.exe", "gpresult.exe", "gpupdate.exe",
        "hostname.exe", "ipconfig.exe", "lodctr.exe", "logman.exe",
        "logoff.exe", "lpq.exe", "lpr.exe", "mode.exe",
        "more.exe", "mountvol.exe", "openfiles.exe", "pagefileconfig.vbs",
        "pathping.exe", "perfmon.exe", "powercfg.exe", "print.exe",
        "proquota.exe", "qprocess.exe", "quser.exe", "qwinsta.exe",
        "rcp.exe", "relog.exe", "ren.exe", "renname.exe",
        "reset.exe", "route.exe", "runas.exe", "rwinsta.exe",
        "sc.exe", "schtasks.exe", "secedit.exe", "setver.exe",
        "share.exe", "shutdown.exe", "sort.exe", "subst.exe",
        "systeminfo.exe", "takeown.exe", "tasklist.exe", "taskkill.exe",
        "timeout.exe", "tree.exe", "tscon.exe", "tsdiscon.exe",
        "tskill.exe", "typeperf.exe", "unlodctr.exe", "verifier.exe",
        "vol.exe", "wevtutil.exe", "whoami.exe", "winmgmt.exe",
        "winver.exe", "wsreset.exe", "wuauclt.exe", "xpsrchvw.exe"
    };

    // 危险目录（不可执行进程的工作目录）
    private static readonly HashSet<string> DangerousWorkingDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Windows",
        @"C:\Windows\System32",
        @"C:\Windows\SysWOW64",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\ProgramData",
    };

    public IReadOnlyCollection<ProcessLogEntry> AuditLog => [.. _auditLog];

    public ProcessGuard(
        string extensionId,
        ILogger logger,
        int maxProcessStartsPerMinute = 10,
        bool blockAllExecutables = true)
    {
        _extensionId = extensionId;
        _logger = logger;
        _maxProcessStartsPerMinute = maxProcessStartsPerMinute;
        _blockAllExecutables = blockAllExecutables;

        _frequencyResetTimer = new Timer(
            callback: _ => ResetFrequency(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// 验证进程创建是否允许。
    /// </summary>
    /// <param name="fileName">可执行文件名或路径</param>
    /// <param name="arguments">进程参数</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <returns>是否允许创建进程</returns>
    public bool ValidateProcessCreation(string fileName, string? arguments = null, string? workingDirectory = null)
    {
        if (_disposed) return false;

        // 1. 检查执行频率
        if (!CheckFrequency())
        {
            LogDenied(fileName, arguments, "进程创建频率超限");
            return false;
        }

        // 2. 检查文件扩展名
        if (!IsExtensionAllowed(fileName))
        {
            LogDenied(fileName, arguments, $"危险可执行文件扩展名: {Path.GetExtension(fileName)}");
            return false;
        }

        // 3. 检查系统工具黑名单
        if (IsDangerousSystemTool(fileName))
        {
            LogDenied(fileName, arguments, $"危险系统工具: {Path.GetFileName(fileName)}");
            return false;
        }

        // 4. 检查工作目录
        if (!string.IsNullOrEmpty(workingDirectory) && IsInDangerousDirectory(workingDirectory))
        {
            LogDenied(fileName, arguments, $"危险工作目录: {workingDirectory}");
            return false;
        }

        // 5. 检查参数中的危险内容
        if (!string.IsNullOrEmpty(arguments) && HasDangerousArguments(arguments))
        {
            LogDenied(fileName, arguments, "进程参数包含危险内容");
            return false;
        }

        // 6. 记录审计日志
        LogAccess(fileName, arguments, workingDirectory);

        return true;
    }

    /// <summary>
    /// 验证 ProcessStartInfo 是否允许。
    /// </summary>
    public bool ValidateProcessStartInfo(ProcessStartInfo startInfo)
    {
        return ValidateProcessCreation(
            startInfo.FileName,
            startInfo.Arguments,
            startInfo.WorkingDirectory);
    }

    /// <summary>
    /// 检查扩展是否可以创建进程。
    /// </summary>
    public bool CanCreateProcess()
    {
        return !_disposed;
    }

    private bool IsExtensionAllowed(string fileName)
    {
        if (!_blockAllExecutables) return true;

        string ext = Path.GetExtension(fileName);
        return !DangerousExtensions.Contains(ext);
    }

    private static bool IsDangerousSystemTool(string fileName)
    {
        string toolName = Path.GetFileName(fileName).ToLowerInvariant();
        return DangerousSystemTools.Contains(toolName);
    }

    private static bool IsInDangerousDirectory(string directory)
    {
        string normalized = Path.GetFullPath(directory);

        foreach (string dangerous in DangerousWorkingDirectories)
        {
            if (string.IsNullOrEmpty(dangerous)) continue;

            if (normalized.StartsWith(dangerous, StringComparison.OrdinalIgnoreCase))
            {
                if (normalized.Length == dangerous.Length ||
                    normalized[dangerous.Length] == Path.DirectorySeparatorChar ||
                    normalized[dangerous.Length] == Path.AltDirectorySeparatorChar)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasDangerousArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return false;

        // 检查长度限制（防止超长参数攻击）
        if (arguments.Length > 4096) return true;

        // 使用正则表达式检测危险模式
        return DangerousArgumentRegex().IsMatch(arguments);
    }

    private bool CheckFrequency()
    {
        lock (_lock)
        {
            string key = _extensionId;
            int count = _processFrequency.AddOrUpdate(key, 1, (_, v) => v + 1);

            if (count > _maxProcessStartsPerMinute)
            {
                _logger.Warn($"[{_extensionId}] 进程创建频率超限: {count} > {_maxProcessStartsPerMinute}");
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
            _processFrequency.Clear();
        }
    }

    private void LogDenied(string fileName, string? arguments, string reason)
    {
        _logger.Warn($"[{_extensionId}] 进程创建拒绝: File={fileName}, Args={arguments}, Reason={reason}");
        _auditLog.Add(new ProcessLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            FileName = fileName,
            Arguments = arguments,
            Allowed = false,
            DenyReason = reason
        });
    }

    private void LogAccess(string fileName, string? arguments, string? workingDirectory)
    {
        _auditLog.Add(new ProcessLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            Allowed = true
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frequencyResetTimer.Dispose();
    }

    /// <summary>进程执行日志条目</summary>
    public sealed class ProcessLogEntry
    {
        public DateTime Timestamp { get; init; }
        public required string ExtensionId { get; init; }
        public required string FileName { get; init; }
        public string? Arguments { get; init; }
        public string? WorkingDirectory { get; init; }
        public bool Allowed { get; init; }
        public string? DenyReason { get; init; }
    }


    /// <summary>
    /// 使用正则表达式检测危险参数（防编码绕过）
    /// </summary>
    [GeneratedRegex(@"(?xi)(\|\|)|(\&&)|(\|)|(>>)|(<>)|(<<)|(\b(cmd|powershell|pwsh|wscript|cscript|mshta)\b)|(-[eE]{1,2}nc\w*\s+)|(-encodedcommand\s+)|(-enc\s+)|(\b(del|rmdir|rd|format|attrib|cacls|icacls|takeown|cipher)\b)|(\b(ftp|telnet|ssh|scp|sftp|curl|wget|bitsadmin)\b)|(\b(taskkill|tasklist|wmic|psexec|ps)\b)|(\b(reg|regedit|regedit32|regsvr32)\b)|(\b(schtasks)\b)|(\bsc\s+(create|delete|start|stop|config)\b)|(\b(net\s+(user|localgroup|group|accounts))\b)|(\b(systeminfo|ipconfig|ifconfig|whoami)\b)|(\b(copy|move|ren|type|xcopy|robocopy)\b)|(\b(set|setx|path|echo)\b\s+)|(\b(assoc|ftype|doskey|label)\b)|(\[Convert\]::FromBase64String)|(\$\{env:)|(\^)|(\\\\[a-zA-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled, "en-US")]
    private static partial Regex DangerousArgumentRegex();
}
