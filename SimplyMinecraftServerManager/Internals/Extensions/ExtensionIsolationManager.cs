using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展数据目录隔离管理器。
/// 防止扩展访问彼此的数据目录。
/// </summary>
internal sealed class ExtensionIsolationManager : IDisposable
{
    private readonly string _baseDataPath;
    private readonly ExtensionLogger? _logger;
    private readonly Timer _cleanupTimer;
    private readonly Lock _lock = new();
    
    // 跟踪扩展数据目录
    private readonly ConcurrentDictionary<string, ExtensionDataInfo> _extensionDataDirs = new();
    
    // 隔离规则
    private readonly Dictionary<string, IsolationRule> _isolationRules = [];
    
    // 事件
    public event EventHandler<IsolationViolationEventArgs>? IsolationViolationDetected;
    
    public ExtensionIsolationManager(
        string baseDataPath,
        ExtensionLogger? logger = null,
        int cleanupIntervalMs = 60000)
    {
        _baseDataPath = baseDataPath;
        _logger = logger;
        
        // 确保基础数据目录存在
        Directory.CreateDirectory(_baseDataPath);
        
        _cleanupTimer = new Timer(
            callback: _ => CleanupIsolationRules(),
            state: null,
            dueTime: cleanupIntervalMs,
            period: cleanupIntervalMs);
    }
    
    /// <summary>
    /// 注册扩展的数据目录。
    /// </summary>
    public void RegisterExtensionDataDirectory(string extensionId, string dataPath)
    {
        if (string.IsNullOrEmpty(extensionId) || string.IsNullOrEmpty(dataPath))
            return;
        
        var fullPath = Path.GetFullPath(dataPath);
        
        // 确保数据目录存在
        Directory.CreateDirectory(fullPath);
        
        var info = new ExtensionDataInfo
        {
            ExtensionId = extensionId,
            DataPath = fullPath,
            RegisteredAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            AccessCount = 0,
        };
        
        _extensionDataDirs[extensionId] = info;
        
        // 创建隔离规则
        lock (_lock)
        {
            _isolationRules[extensionId] = new IsolationRule
            {
                ExtensionId = extensionId,
                AllowedPaths = [fullPath],
                DeniedPaths = [.. _extensionDataDirs.Values
                    .Where(d => d.ExtensionId != extensionId)
                    .Select(d => d.DataPath)],
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
            };
        }
        
        _logger?.Info($"已为扩展 {extensionId} 注册数据目录: {fullPath}");
    }
    
    /// <summary>
    /// 注销扩展的数据目录。
    /// </summary>
    public void UnregisterExtensionDataDirectory(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
            return;
        
        _extensionDataDirs.TryRemove(extensionId, out _);
        
        // 更新其他扩展的拒绝路径
        lock (_lock)
        {
            _isolationRules.Remove(extensionId);
            
            foreach (var rule in _isolationRules.Values)
            {
                if (rule.ExtensionId != extensionId)
                {
                    var deniedPaths = rule.DeniedPaths
                        .Where(p => !p.Contains(extensionId))
                        .ToArray();
                    
                    _isolationRules[rule.ExtensionId] = new IsolationRule
                    {
                        ExtensionId = rule.ExtensionId,
                        AllowedPaths = rule.AllowedPaths,
                        DeniedPaths = deniedPaths,
                        CreatedAt = rule.CreatedAt,
                        LastUpdated = DateTime.UtcNow,
                    };
                }
            }
        }
        
        _logger?.Info($"已注销扩展 {extensionId} 的数据目录");
    }
    
    /// <summary>
    /// 验证扩展是否可以访问指定文件路径。
    /// 返回 true 表示允许访问，false 表示应阻止。
    /// </summary>
    public bool ValidateFileAccess(string extensionId, string filePath)
    {
        if (string.IsNullOrEmpty(extensionId) || string.IsNullOrEmpty(filePath))
            return false;
        
        var fullPath = Path.GetFullPath(filePath);
        
        // 检查路径是否在扩展自己的数据目录内
        if (_extensionDataDirs.TryGetValue(extensionId, out var dataInfo))
        {
            if (fullPath.StartsWith(dataInfo.DataPath, StringComparison.OrdinalIgnoreCase))
            {
                // 更新访问信息
                dataInfo.LastAccessedAt = DateTime.UtcNow;
                dataInfo.AccessCount++;
                return true;
            }
        }
        
        // 检查隔离规则
        lock (_lock)
        {
            if (_isolationRules.TryGetValue(extensionId, out var rule))
            {
                // 检查路径是否在允许路径列表中
                bool isAllowed = rule.AllowedPaths.Any(allowedPath => 
                    fullPath.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase));
                
                if (isAllowed)
                {
                    return true;
                }
                
                // 检查路径是否在拒绝路径列表中
                bool isDenied = rule.DeniedPaths.Any(deniedPath => 
                    fullPath.StartsWith(deniedPath, StringComparison.OrdinalIgnoreCase));
                
                if (isDenied)
                {
                    OnIsolationViolationDetected(extensionId, filePath, "访问其他扩展的数据目录");
                    return false;
                }
            }
        }
        
        // 默认：允许访问非扩展目录
        return true;
    }
    
    /// <summary>
    /// 获取扩展数据目录的信息。
    /// </summary>
    public ExtensionDataInfo? GetExtensionDataInfo(string extensionId)
    {
        _extensionDataDirs.TryGetValue(extensionId, out var info);
        return info;
    }
    
    /// <summary>
    /// 获取所有已注册的扩展数据目录。
    /// </summary>
    public IReadOnlyList<ExtensionDataInfo> GetAllExtensionDataDirectories()
    {
        return _extensionDataDirs.Values.ToList().AsReadOnly();
    }
    
    /// <summary>
    /// 获取扩展的隔离规则。
    /// </summary>
    public IsolationRule? GetIsolationRule(string extensionId)
    {
        lock (_lock)
        {
            _isolationRules.TryGetValue(extensionId, out var rule);
            return rule;
        }
    }
    
    /// <summary>
    /// 更新扩展的隔离规则。
    /// </summary>
    public void UpdateIsolationRule(string extensionId, string[] allowedPaths, string[] deniedPaths)
    {
        if (string.IsNullOrEmpty(extensionId))
            return;
        
        lock (_lock)
        {
            _isolationRules[extensionId] = new IsolationRule
            {
                ExtensionId = extensionId,
                AllowedPaths = allowedPaths,
                DeniedPaths = deniedPaths,
                CreatedAt = _isolationRules.TryGetValue(extensionId, out var existingRule) ? existingRule.CreatedAt : DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
            };
        }
        
        _logger?.Info($"已更新扩展 {extensionId} 的隔离规则");
    }
    
    /// <summary>
    /// 获取扩展数据目录的总大小。
    /// </summary>
    public long GetExtensionDataSize(string extensionId)
    {
        if (!_extensionDataDirs.TryGetValue(extensionId, out var dataInfo))
            return 0;
        
        try
        {
            return GetDirectorySize(dataInfo.DataPath);
        }
        catch
        {
            return 0;
        }
    }
    
    /// <summary>
    /// 获取所有扩展数据目录的总大小。
    /// </summary>
    public long GetAllExtensionsDataSize()
    {
        return _extensionDataDirs.Values.Sum(d => GetDirectorySize(d.DataPath));
    }
    
    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;
        
        long size = 0;
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    size += fileInfo.Length;
                }
                catch
                {
                    // 跳过无法访问的文件
                }
            }
        }
        catch
        {
            // 跳过无法访问的目录
        }
        
        return size;
    }
    
    private void CleanupIsolationRules()
    {
        lock (_lock)
        {
            // 移除未注册扩展的规则
            var keysToRemove = _isolationRules.Keys
                .Where(key => !_extensionDataDirs.ContainsKey(key))
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _isolationRules.Remove(key);
            }
            
            // 更新剩余规则的拒绝路径
            var allDataPaths = _extensionDataDirs.Values.Select(d => d.DataPath).ToList();
            
            foreach (var rule in _isolationRules.Values.ToList())
            {
                if (_extensionDataDirs.TryGetValue(rule.ExtensionId, out var dataInfo))
                {
                    var deniedPaths = allDataPaths
                        .Where(p => !p.StartsWith(dataInfo.DataPath, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    
                    _isolationRules[rule.ExtensionId] = new IsolationRule
                    {
                        ExtensionId = rule.ExtensionId,
                        AllowedPaths = rule.AllowedPaths,
                        DeniedPaths = deniedPaths,
                        CreatedAt = rule.CreatedAt,
                        LastUpdated = DateTime.UtcNow,
                    };
                }
            }
        }
    }
    
    private void OnIsolationViolationDetected(string extensionId, string filePath, string reason)
    {
        _logger?.Warn($"检测到扩展 {extensionId} 的隔离违规: {reason} - {filePath}");
        
        IsolationViolationDetected?.Invoke(this, new IsolationViolationEventArgs
        {
            ExtensionId = extensionId,
            FilePath = filePath,
            Reason = reason,
            Timestamp = DateTime.UtcNow,
        });
    }
    
    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
    
    /// <summary>
    /// 扩展数据信息。
    /// </summary>
    public sealed class ExtensionDataInfo
    {
        public required string ExtensionId { get; init; }
        public required string DataPath { get; init; }
        public DateTime RegisteredAt { get; init; }
        public DateTime LastAccessedAt { get; set; }
        public int AccessCount { get; set; }
    }
    
    /// <summary>
    /// 扩展的隔离规则。
    /// </summary>
    public sealed class IsolationRule
    {
        public required string ExtensionId { get; init; }
        public required string[] AllowedPaths { get; init; }
        public required string[] DeniedPaths { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime LastUpdated { get; init; }
    }
    
    /// <summary>
    /// 隔离违规事件参数。
    /// </summary>
    public sealed class IsolationViolationEventArgs : EventArgs
    {
        public required string ExtensionId { get; init; }
        public required string FilePath { get; init; }
        public required string Reason { get; init; }
        public DateTime Timestamp { get; init; }
    }
}
