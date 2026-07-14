// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 文件访问守卫：校验所有文件操作是否在声明的范围内。
///   1. 危险系统目录过滤（硬编码，不可绕过）
///   2. 路径穿越检测
///   3. 声明范围校验
///   4. 文件扩展名过滤
///   5. 操作审计日志
/// </summary>
internal sealed class FileAccessGuard
{
    private readonly string _extensionId;
    private readonly ILogger _logger;
    private readonly Dictionary<string, FileAccessScope> _declaredScopes;
    private readonly string _extensionDataPath;
    private readonly ConcurrentBag<FileAccessLogEntry> _auditLog = new();

    // 危险系统目录（硬编码，不可覆盖）
    private static readonly HashSet<string> DangerousDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Windows",
        @"C:\Windows\System32",
        @"C:\Windows\SysWOW64",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
        @"C:\ProgramData",
        @"C:\Recovery",
        @"C:\$Recycle.Bin",
        @"C:\System Volume Information",
        @"C:\Boot",
        @"C:\bootmgr",
        @"C:\pagefile.sys",
        @"C:\hiberfil.sys",
        @"C:\swapfile.sys",
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    };

    // 危险文件名模式
    private static readonly HashSet<string> DangerousFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "hosts", "passwd", "shadow", "sudoers",
        "SAM", "SYSTEM", "SECURITY", "SOFTWARE",
    };

    // 危险可执行文件扩展名（硬编码，不可覆盖）
    private static readonly HashSet<string> DangerousExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".msi", ".msp", ".mst", ".pif",
        ".bat", ".cmd", ".com", ".cpl", ".hta", ".inf", ".ins",
        ".isp", ".jse", ".lnk", ".msc", ".msi", ".msp", ".mst",
        ".pif", ".ps1", ".ps2", ".psm1", ".psc1", ".psc2",
        ".reg", ".rgs", ".scf", ".scm", ".sct", ".shb", ".shs",
        ".vbe", ".vbs", ".vbscript", ".ws", ".wsc", ".wsf", ".wsh",
        ".xbap", ".xnk", ".appx", ".appxbundle", ".msix", ".msixbundle",
    };

    public IReadOnlyCollection<FileAccessLogEntry> AuditLog => _auditLog.ToArray();

    public FileAccessGuard(
        string extensionId,
        ILogger logger,
        IReadOnlyList<FileAccessScope> declaredScopes,
        string extensionDataPath)
    {
        _extensionId = extensionId;
        _logger = logger;
        _extensionDataPath = extensionDataPath;

        _declaredScopes = declaredScopes.ToDictionary(
            s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 校验文件操作是否允许。
    /// </summary>
    /// <returns>允许的绝对路径，或 null 表示拒绝</returns>
    public string? Validate(string scopeId, string relativePath, FileAccessLevel requiredLevel)
    {
        // 1. 查找声明的 scope
        if (!_declaredScopes.TryGetValue(scopeId, out var scope))
        {
            LogDenied(scopeId, relativePath, "未声明的访问范围");
            return null;
        }

        // 2. 检查访问级别
        if (!scope.Level.HasFlag(requiredLevel))
        {
            LogDenied(scopeId, relativePath, $"请求 {requiredLevel}，声明级别 {scope.Level}");
            return null;
        }

        // 3. 解析绝对路径
        string absolutePath = ResolvePath(scope, relativePath);

        // 4. 路径穿越检测（检查路径是否在声明的范围内）
        if (!IsPathWithinScope(absolutePath, scope))
        {
            LogDenied(scopeId, relativePath, $"路径穿越检测: {absolutePath} 不在声明范围内");
            return null;
        }

        // 4.1 符号链接/Junction 检测
        if (IsSymlinkOrJunction(absolutePath))
        {
            LogDenied(scopeId, relativePath, $"检测到符号链接/Junction: {absolutePath}");
            return null;
        }

        // 5. 危险目录过滤
        if (IsInDangerousDirectory(absolutePath))
        {
            LogDenied(scopeId, relativePath, $"访问危险系统目录: {absolutePath}");
            return null;
        }

        // 6. 危险文件名过滤
        string fileName = Path.GetFileName(absolutePath);
        if (DangerousFileNames.Contains(fileName))
        {
            LogDenied(scopeId, relativePath, $"访问危险系统文件: {fileName}");
            return null;
        }

        // 7. 文件扩展名过滤
        if (!IsExtensionAllowed(scope, absolutePath))
        {
            LogDenied(scopeId, relativePath, $"文件扩展名被拒绝: {Path.GetExtension(absolutePath)}");
            return null;
        }

        // 8. UNC 路径检测
        if (IsUncPath(absolutePath))
        {
            LogDenied(scopeId, relativePath, $"UNC 路径访问被拒绝: {absolutePath}");
            return null;
        }

        // 9. NTFS 流检测
        if (HasNtfsStream(absolutePath))
        {
            LogDenied(scopeId, relativePath, $"NTFS 备用数据流访问被拒绝: {absolutePath}");
            return null;
        }

        // 10. 记录审计日志
        LogAccess(scopeId, absolutePath, requiredLevel);

        return absolutePath;
    }

    private string ResolvePath(FileAccessScope scope, string relativePath)
    {
        // 解析特殊路径变量
        string basePath = scope.Paths.Length > 0 ? ResolveSpecialPath(scope.Paths[0]) : _extensionDataPath;

        // 如果有多个路径，尝试匹配
        if (scope.Paths.Length > 1)
        {
            foreach (string declaredPath in scope.Paths)
            {
                string resolved = ResolveSpecialPath(declaredPath);
                string combined = Path.Combine(resolved, relativePath);
                if (File.Exists(combined) || Directory.Exists(Path.GetDirectoryName(combined)))
                {
                    return Path.GetFullPath(combined);
                }
            }
        }

        return Path.GetFullPath(Path.Combine(basePath, relativePath));
    }

    private string ResolveSpecialPath(string path)
    {
        if (path.StartsWith("${extensionData}", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(_extensionDataPath, _extensionId);
        }
        if (path.StartsWith("${instanceRoot}", StringComparison.OrdinalIgnoreCase))
        {
            return PathHelper.InstancesRoot;
        }
        if (path.StartsWith("${instance:", StringComparison.OrdinalIgnoreCase))
        {
            string instanceId = path["${instance:".Length..].TrimEnd('}');
            return PathHelper.GetInstanceDir(instanceId);
        }
        if (path.StartsWith("~", StringComparison.Ordinal))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// 检查路径是否在声明的访问范围内（修复路径穿越检测缺陷）
    /// </summary>
    private static bool IsPathWithinScope(string absolutePath, FileAccessScope scope)
    {
        string normalized = Path.GetFullPath(absolutePath);

        // 检查路径是否在声明的任一目录内
        foreach (string declaredPath in scope.Paths)
        {
            string resolvedBase = Path.GetFullPath(declaredPath);
            if (normalized.StartsWith(resolvedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(resolvedBase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检测路径是否是符号链接或 Junction（P1: 符号链接检测）
    /// </summary>
    private static bool IsSymlinkOrJunction(string absolutePath)
    {
        try
        {
            if (File.Exists(absolutePath))
            {
                var fileInfo = new FileInfo(absolutePath);
                return (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0;
            }

            if (Directory.Exists(absolutePath))
            {
                var dirInfo = new DirectoryInfo(absolutePath);
                return (dirInfo.Attributes & FileAttributes.ReparsePoint) != 0;
            }
        }
        catch
        {
            // 如果无法检查属性，假设是安全的（让后续检查处理）
        }

        return false;
    }

    /// <summary>
    /// 检测 UNC 路径访问（\\server\share 形式）
    /// </summary>
    private static bool IsUncPath(string absolutePath)
    {
        return absolutePath.StartsWith(@"\\", StringComparison.Ordinal) ||
               absolutePath.StartsWith("//", StringComparison.Ordinal);
    }

    /// <summary>
    /// 检测 NTFS 备用数据流访问（file.txt:hidden:$DATA 形式）
    /// </summary>
    private static bool HasNtfsStream(string absolutePath)
    {
        // NTFS 流包含冒号分隔符（但跳过驱动器号如 C:）
        int colonIndex = absolutePath.IndexOf(':');
        if (colonIndex > 0)
        {
            // 检查是否是驱动器号（如 C:）还是流分隔符
            if (colonIndex == 1 && char.IsLetter(absolutePath[0]))
            {
                return false; // 这是驱动器号
            }

            // 检查是否有额外的冒号（表示 NTFS 流）
            int secondColon = absolutePath.IndexOf(':', colonIndex + 1);
            if (secondColon > 0)
            {
                return true; // 包含 NTFS 流
            }
        }

        return false;
    }

    private static bool IsInDangerousDirectory(string absolutePath)
    {
        string normalized = Path.GetFullPath(absolutePath);

        foreach (string dangerous in DangerousDirectories)
        {
            if (string.IsNullOrEmpty(dangerous)) continue;

            if (normalized.StartsWith(dangerous, StringComparison.OrdinalIgnoreCase))
            {
                // 允许精确匹配（如 C:\Windows 本身不算穿越）
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

    private static bool IsExtensionAllowed(FileAccessScope scope, string absolutePath)
    {
        string ext = Path.GetExtension(absolutePath);

        // 1. 危险可执行文件扩展名（硬编码，不可覆盖）
        if (DangerousExecutableExtensions.Contains(ext))
        {
            return false;
        }

        // 2. 禁止列表优先
        if (scope.DeniedExtensions.Length > 0 &&
            scope.DeniedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // 3. 允许列表为空表示允许所有
        if (scope.AllowedExtensions.Length == 0)
        {
            return true;
        }

        return scope.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private void LogDenied(string scopeId, string path, string reason)
    {
        _logger.Warn($"[{_extensionId}] 文件访问拒绝: Scope={scopeId}, Path={path}, Reason={reason}");
        _auditLog.Add(new FileAccessLogEntry
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
        _auditLog.Add(new FileAccessLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            ScopeId = scopeId,
            Path = path,
            Level = level,
            Allowed = true
        });
    }

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
}
