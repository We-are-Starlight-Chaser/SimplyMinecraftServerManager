// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 文件访问守卫
/// 校验所有文件操作是否在声明范围内，支持 TOCTOU 保护和审计日志。
/// </summary>
internal sealed class FileAccessGuard : IDisposable
{
    private readonly string _extensionId;
    private readonly ILogger _logger;
    private readonly Dictionary<string, FileAccessScope> _declaredScopes;
    private readonly string _extensionDataPath;

    // 审计日志：环形缓冲区，防止无限增长
    private readonly FileAccessLogEntry[] _auditRing;
    private int _auditHead;
    private int _auditCount;
    private readonly Lock _auditLock = new();
    private const int MaxAuditEntries = 1024;

    // TOCTOU 跟踪器
    private readonly ConcurrentDictionary<string, FileOperationTracker> _trackers = new();
    private readonly Timer _cleanupTimer;
    private int _disposed;

    // 预计算危险目录的规范化形式
    private static readonly string[] DangerousDirs;
    private static readonly HashSet<string> DangerousFileNames;
    private static readonly HashSet<string> DangerousExeExts;

    static FileAccessGuard()
    {
        var rawDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows", @"C:\Windows\System32", @"C:\Windows\SysWOW64",
            @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData",
            @"C:\Recovery", @"C:\$Recycle.Bin", @"C:\System Volume Information",
            @"C:\Boot",
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };

        // 预规范化 + 确保尾部带分隔符（修复 StartsWith 边界缺陷）
        DangerousDirs = [.. rawDirs
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => EnsureTrailingSeparator(Path.GetFullPath(d)))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        DangerousFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hosts", "passwd", "shadow", "sudoers",
            "SAM", "SYSTEM", "SECURITY", "SOFTWARE",
        };

        DangerousExeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".scr", ".msi", ".msp", ".mst", ".pif",
            ".bat", ".cmd", ".cpl", ".hta", ".inf", ".ins",
            ".isp", ".jse", ".lnk", ".msc", ".ps1", ".ps2",
            ".psm1", ".psc1", ".psc2", ".reg", ".rgs", ".scf",
            ".scm", ".sct", ".shb", ".shs", ".vbe", ".vbs",
            ".vbscript", ".ws", ".wsc", ".wsf", ".wsh",
            ".xbap", ".xnk", ".appx", ".appxbundle", ".msix", ".msixbundle",
        };
    }

    public FileAccessGuard(
        string extensionId,
        ILogger logger,
        IReadOnlyList<FileAccessScope> declaredScopes,
        string extensionDataPath)
    {
        _extensionId = extensionId;
        _logger = logger;
        _declaredScopes = declaredScopes.ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        _extensionDataPath = extensionDataPath;
        _auditRing = new FileAccessLogEntry[MaxAuditEntries];

        // 每 5 分钟自动清理过期 tracker
        _cleanupTimer = new Timer(_ => CleanupTrackers(), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>获取审计日志快照</summary>
    public IReadOnlyList<FileAccessLogEntry> GetAuditLog()
    {
        lock (_auditLock)
        {
            if (_auditCount == 0) return [];
            var result = new List<FileAccessLogEntry>(_auditCount);
            int start = (_auditHead - _auditCount + MaxAuditEntries) % MaxAuditEntries;
            for (int i = 0; i < _auditCount; i++)
                result.Add(_auditRing[(start + i) % MaxAuditEntries]);
            return result;
        }
    }

    /// <summary>
    /// 校验文件操作是否允许。返回允许的绝对路径，或 null 表示拒绝。
    /// </summary>
    public string? Validate(string scopeId, string relativePath, FileAccessLevel requiredLevel)
    {
        // 1. Scope 查找
        if (!_declaredScopes.TryGetValue(scopeId, out var scope))
        {
            LogDenied(scopeId, relativePath, "未声明的访问范围");
            return null;
        }

        // 2. 权限级别
        if (!scope.Level.HasFlag(requiredLevel))
        {
            LogDenied(scopeId, relativePath, $"请求 {requiredLevel}，声明 {scope.Level}");
            return null;
        }

        // 3. 解析 + 归一化（只做一次 GetFullPath）
        string absolutePath = ResolveAndNormalize(scope, relativePath);

        // 4. 路径穿越检测（使用预计算的 scope 基路径）
        if (!IsWithinAnyScopeBase(absolutePath, scope))
        {
            LogDenied(scopeId, relativePath, $"路径穿越: {absolutePath}");
            return null;
        }

        // 5. 符号链接/Junction
        if (IsSymlinkOrJunction(absolutePath))
        {
            LogDenied(scopeId, relativePath, $"符号链接/Junction: {absolutePath}");
            return null;
        }

        // 6. 危险目录（使用预规范化 + 尾部精确匹配）
        if (IsInDangerousDirectory(absolutePath))
        {
            LogDenied(scopeId, relativePath, $"危险系统目录: {absolutePath}");
            return null;
        }

        // 7. 危险文件名
        string fileName = Path.GetFileName(absolutePath);
        if (DangerousFileNames.Contains(fileName))
        {
            LogDenied(scopeId, relativePath, $"危险系统文件: {fileName}");
            return null;
        }

        // 8. 扩展名过滤
        if (!IsExtensionAllowed(scope, absolutePath))
        {
            LogDenied(scopeId, relativePath, $"扩展名被拒: {Path.GetExtension(absolutePath)}");
            return null;
        }

        // 9. UNC 路径
        if (absolutePath.StartsWith(@"\\", StringComparison.Ordinal) ||
            absolutePath.StartsWith("//", StringComparison.Ordinal))
        {
            LogDenied(scopeId, relativePath, $"UNC 路径被拒: {absolutePath}");
            return null;
        }

        // 10. NTFS 备用数据流
        if (HasNtfsStream(absolutePath))
        {
            LogDenied(scopeId, relativePath, $"NTFS 流被拒: {absolutePath}");
            return null;
        }

        LogAccess(scopeId, absolutePath, requiredLevel);
        return absolutePath;
    }

    // ===== 路径解析（合并 Resolve + Normalize，消除重复 GetFullPath） =====
    private string ResolveAndNormalize(FileAccessScope scope, string relativePath)
    {
        string basePath = scope.Paths.Length > 0
            ? ResolveSpecialPath(scope.Paths[0])
            : _extensionDataPath;

        if (scope.Paths.Length > 1)
        {
            foreach (string declared in scope.Paths)
            {
                string resolved = ResolveSpecialPath(declared);
                string combined = Path.Combine(resolved, relativePath);
                if (File.Exists(combined) || Directory.Exists(Path.GetDirectoryName(combined)!))
                    return Path.GetFullPath(combined);
            }
        }

        return Path.GetFullPath(Path.Combine(basePath, relativePath));
    }

    private string ResolveSpecialPath(string path)
    {
        if (path.StartsWith("${extensionData}", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_extensionDataPath, _extensionId);
        if (path.StartsWith("${instanceRoot}", StringComparison.OrdinalIgnoreCase))
            return PathHelper.InstancesRoot;
        if (path.StartsWith("${instance:", StringComparison.OrdinalIgnoreCase))
        {
            string instanceId = path["${instance:".Length..].TrimEnd('}');
            return PathHelper.GetInstanceDir(instanceId);
        }
        if (path.StartsWith('~'))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return Path.GetFullPath(path);
    }

    // ===== 路径穿越检测（使用预缓存的 scope 基路径） =====
    private bool IsWithinAnyScopeBase(string normalizedPath, FileAccessScope scope)
    {
        foreach (string declared in scope.Paths)
        {
            string baseDir = EnsureTrailingSeparator(Path.GetFullPath(ResolveSpecialPath(declared)));
            if (normalizedPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ===== 危险目录检测（修复 StartsWith 边界缺陷） =====
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInDangerousDirectory(string normalizedPath)
    {
        foreach (string dir in DangerousDirs)
        {
            // dir 已带尾部分隔符，StartsWith 天然保证边界
            if (normalizedPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
            return path;
        return path + Path.DirectorySeparatorChar;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSymlinkOrJunction(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch { return false; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasNtfsStream(string path)
    {
        // 跳过驱动器号 C:，检查后续是否还有冒号
        int first = path.IndexOf(':');
        if (first <= 0) return false;
        return path.IndexOf(':', first + 1) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExtensionAllowed(FileAccessScope scope, string path)
    {
        string ext = Path.GetExtension(path);
        if (DangerousExeExts.Contains(ext)) return false;
        if (scope.DeniedExtensions.Length > 0 &&
            scope.DeniedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return false;
        if (scope.AllowedExtensions.Length == 0) return true;
        return scope.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    // ===== 环形缓冲审计日志（固定内存，零 GC 压力） =====
    private void LogDenied(string scopeId, string path, string reason)
    {
        _logger.Warn($"[{_extensionId}] 文件拒绝: Scope={scopeId}, Path={path}, Reason={reason}");
        AddAuditEntry(new FileAccessLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            ScopeId = scopeId,
            Path = path,
            Allowed = false,
            DenyReason = reason
        });
    }

    private void LogAccess(string scopeId, string path, FileAccessLevel level)
    {
        AddAuditEntry(new FileAccessLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            ScopeId = scopeId,
            Path = path,
            Level = level,
            Allowed = true
        });
    }

    private void AddAuditEntry(FileAccessLogEntry entry)
    {
        lock (_auditLock)
        {
            _auditRing[_auditHead] = entry;
            _auditHead = (_auditHead + 1) % MaxAuditEntries;
            if (_auditCount < MaxAuditEntries) _auditCount++;
        }
    }

    // ===== TOCTOU 跟踪 =====
    public bool TrackFileOperation(string filePath, FileAccessLevel level)
    {
        var tracker = _trackers.GetOrAdd(filePath,
            p => new FileOperationTracker(p, _logger));
        if (tracker.HasFileBeenModified())
        {
            LogDenied("TOCTOU", filePath, "TOCTOU race condition detected");
            return true;
        }
        return false;
    }

    public FileStream? AcquireFileLock(string filePath, FileAccess access, FileShare share)
    {
        var tracker = _trackers.GetOrAdd(filePath,
            p => new FileOperationTracker(p, _logger));
        return tracker.AcquireLock(access, share);
    }

    private void CleanupTrackers()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(10);
        foreach (var kvp in _trackers)
        {
            if (kvp.Value.LastCheckTime < cutoff)
            {
                if (_trackers.TryRemove(kvp.Key, out var removed))
                    removed.Dispose();
            }
        }
    }

    // ===== Dispose：释放所有资源 =====
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        _cleanupTimer.Dispose();

        // 释放所有 tracker（包括其中可能持有的 FileStream）
        foreach (var kvp in _trackers)
        {
            if (_trackers.TryRemove(kvp.Key, out var tracker))
                tracker.Dispose();
        }
        _trackers.Clear();
    }

    // ===== 数据模型 =====
    public sealed class FileAccessLogEntry
    {
        public DateTime Timestamp { get; init; }
        public required string ExtensionId { get; init; }
        public required string ScopeId { get; init; }
        public required string Path { get; init; }
        public FileAccessLevel Level { get; init; }
        public bool Allowed { get; init; }
        public string? DenyReason { get; init; }
    }

    /// <summary>
    /// 文件操作跟踪器（实现 IDisposable 释放锁定的 FileStream）。
    /// </summary>
    private sealed class FileOperationTracker : IDisposable
    {
        private readonly string _filePath;
        private readonly ILogger _logger;
        private DateTime _lastCheckTime;
        private FileAttributes _lastAttributes;
        private long _lastSize;
        private int _checkCount;
        private FileStream? _heldLock;
        private readonly Lock _lock = new();
        private int _disposed;

        public DateTime LastCheckTime => _lastCheckTime;

        public FileOperationTracker(string filePath, ILogger logger)
        {
            _filePath = filePath;
            _logger = logger;
            _lastCheckTime = DateTime.UtcNow;
            try
            {
                if (File.Exists(filePath))
                {
                    var fi = new FileInfo(filePath);
                    _lastAttributes = fi.Attributes;
                    _lastSize = fi.Length;
                }
            }
            catch { /* 初始化失败不阻塞 */ }
        }

        public bool HasFileBeenModified()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_filePath)) return true;

                    var fi = new FileInfo(_filePath);
                    var curAttrs = fi.Attributes;
                    var curSize = fi.Length;
                    var now = DateTime.UtcNow;

                    bool modified = curAttrs != _lastAttributes || curSize != _lastSize;

                    if (!modified && (now - _lastCheckTime) < TimeSpan.FromSeconds(5))
                    {
                        _checkCount++;
                        if (_checkCount > 10)
                        {
                            _logger.Warn($"TOCTOU: 短时间内过多检查 {_filePath}");
                            return true;
                        }
                    }
                    else
                    {
                        _checkCount = 1;
                    }

                    if (modified)
                        _logger.Warn($"TOCTOU: 文件变更 {_filePath}");

                    _lastCheckTime = now;
                    _lastAttributes = curAttrs;
                    _lastSize = curSize;
                    return modified;
                }
                catch { return false; }
            }
        }

        public FileStream? AcquireLock(FileAccess access, FileShare share)
        {
            lock (_lock)
            {
                // 释放旧锁再获取新锁，防止句柄泄漏
                _heldLock?.Dispose();
                _heldLock = null;

                try
                {
                    _heldLock = new FileStream(_filePath, FileMode.Open, access, share, 4096, FileOptions.None);
                    return _heldLock;
                }
                catch { return null; }
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
            lock (_lock)
            {
                _heldLock?.Dispose();
                _heldLock = null;
            }
        }
    }
}