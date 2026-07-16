using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// Manager for isolating extension data directories.
/// Prevents extensions from accessing each other's data.
/// </summary>
internal sealed class ExtensionIsolationManager : IDisposable
{
    private readonly string _baseDataPath;
    private readonly ExtensionLogger? _logger;
    private readonly Timer _cleanupTimer;
    private readonly Lock _lock = new();
    
    // Track extension data directories
    private readonly ConcurrentDictionary<string, ExtensionDataInfo> _extensionDataDirs = new();
    
    // Isolation rules
    private readonly Dictionary<string, IsolationRule> _isolationRules = new();
    
    // Events
    public event EventHandler<IsolationViolationEventArgs>? IsolationViolationDetected;
    
    public ExtensionIsolationManager(
        string baseDataPath,
        ExtensionLogger? logger = null,
        int cleanupIntervalMs = 60000)
    {
        _baseDataPath = baseDataPath;
        _logger = logger;
        
        // Ensure base data directory exists
        Directory.CreateDirectory(_baseDataPath);
        
        _cleanupTimer = new Timer(
            callback: _ => CleanupIsolationRules(),
            state: null,
            dueTime: cleanupIntervalMs,
            period: cleanupIntervalMs);
    }
    
    /// <summary>
    /// Registers an extension's data directory.
    /// </summary>
    public void RegisterExtensionDataDirectory(string extensionId, string dataPath)
    {
        if (string.IsNullOrEmpty(extensionId) || string.IsNullOrEmpty(dataPath))
            return;
        
        var fullPath = Path.GetFullPath(dataPath);
        
        // Ensure the data directory exists
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
        
        // Create isolation rule
        _isolationRules[extensionId] = new IsolationRule
        {
            ExtensionId = extensionId,
            AllowedPaths = new[] { fullPath },
            DeniedPaths = _extensionDataDirs.Values
                .Where(d => d.ExtensionId != extensionId)
                .Select(d => d.DataPath)
                .ToArray(),
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
        };
        
        _logger?.Info($"Registered data directory for extension {extensionId}: {fullPath}");
    }
    
    /// <summary>
    /// Unregisters an extension's data directory.
    /// </summary>
    public void UnregisterExtensionDataDirectory(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
            return;
        
        _extensionDataDirs.TryRemove(extensionId, out _);
        _isolationRules.Remove(extensionId);
        
        // Update other extensions' denied paths
        lock (_lock)
        {
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
        
        _logger?.Info($"Unregistered data directory for extension {extensionId}");
    }
    
    /// <summary>
    /// Validates if an extension can access a file path.
    /// Returns true if access is allowed, false if it should be blocked.
    /// </summary>
    public bool ValidateFileAccess(string extensionId, string filePath)
    {
        if (string.IsNullOrEmpty(extensionId) || string.IsNullOrEmpty(filePath))
            return false;
        
        var fullPath = Path.GetFullPath(filePath);
        
        // Check if path is within the extension's own data directory
        if (_extensionDataDirs.TryGetValue(extensionId, out var dataInfo))
        {
            if (fullPath.StartsWith(dataInfo.DataPath, StringComparison.OrdinalIgnoreCase))
            {
                // Update access info
                dataInfo.LastAccessedAt = DateTime.UtcNow;
                dataInfo.AccessCount++;
                return true;
            }
        }
        
        // Check isolation rules
        if (_isolationRules.TryGetValue(extensionId, out var rule))
        {
            // Check if path is in allowed paths
            bool isAllowed = rule.AllowedPaths.Any(allowedPath => 
                fullPath.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase));
            
            if (isAllowed)
            {
                return true;
            }
            
            // Check if path is in denied paths
            bool isDenied = rule.DeniedPaths.Any(deniedPath => 
                fullPath.StartsWith(deniedPath, StringComparison.OrdinalIgnoreCase));
            
            if (isDenied)
            {
                OnIsolationViolationDetected(extensionId, filePath, "Access to other extension's data directory");
                return false;
            }
        }
        
        // Default: allow access to non-extension directories
        return true;
    }
    
    /// <summary>
    /// Gets information about an extension's data directory.
    /// </summary>
    public ExtensionDataInfo? GetExtensionDataInfo(string extensionId)
    {
        _extensionDataDirs.TryGetValue(extensionId, out var info);
        return info;
    }
    
    /// <summary>
    /// Gets all registered extension data directories.
    /// </summary>
    public IReadOnlyList<ExtensionDataInfo> GetAllExtensionDataDirectories()
    {
        return _extensionDataDirs.Values.ToList().AsReadOnly();
    }
    
    /// <summary>
    /// Gets the isolation rule for an extension.
    /// </summary>
    public IsolationRule? GetIsolationRule(string extensionId)
    {
        _isolationRules.TryGetValue(extensionId, out var rule);
        return rule;
    }
    
    /// <summary>
    /// Updates isolation rules for an extension.
    /// </summary>
    public void UpdateIsolationRule(string extensionId, string[] allowedPaths, string[] deniedPaths)
    {
        if (string.IsNullOrEmpty(extensionId))
            return;
        
        _isolationRules[extensionId] = new IsolationRule
        {
            ExtensionId = extensionId,
            AllowedPaths = allowedPaths,
            DeniedPaths = deniedPaths,
            CreatedAt = _isolationRules.TryGetValue(extensionId, out var existingRule) ? existingRule.CreatedAt : DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
        };
        
        _logger?.Info($"Updated isolation rule for extension {extensionId}");
    }
    
    /// <summary>
    /// Gets the total size of an extension's data directory.
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
    /// Gets the total size of all extension data directories.
    /// </summary>
    public long GetAllExtensionsDataSize()
    {
        return _extensionDataDirs.Values.Sum(d => GetDirectorySize(d.DataPath));
    }
    
    private long GetDirectorySize(string path)
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
                    // Skip files that can't be accessed
                }
            }
        }
        catch
        {
            // Skip directories that can't be accessed
        }
        
        return size;
    }
    
    private void CleanupIsolationRules()
    {
        lock (_lock)
        {
            // Remove rules for unregistered extensions
            var keysToRemove = _isolationRules.Keys
                .Where(key => !_extensionDataDirs.ContainsKey(key))
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _isolationRules.Remove(key);
            }
            
            // Update denied paths for remaining rules
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
        _logger?.Warn($"Isolation violation detected for extension {extensionId}: {reason} - {filePath}");
        
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
    /// Extension data information.
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
    /// Isolation rule for an extension.
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
    /// Isolation violation event arguments.
    /// </summary>
    public sealed class IsolationViolationEventArgs : EventArgs
    {
        public required string ExtensionId { get; init; }
        public required string FilePath { get; init; }
        public required string Reason { get; init; }
        public DateTime Timestamp { get; init; }
    }
}
