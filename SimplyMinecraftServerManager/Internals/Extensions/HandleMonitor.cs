using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// Monitor for tracking file and process handle leaks in extensions.
/// Detects when extensions don't properly release handles.
/// </summary>
internal sealed class HandleMonitor : IDisposable
{
    private readonly string _extensionId;
    private readonly ExtensionLogger? _logger;
    private readonly Timer _monitorTimer;
    private readonly Lock _lock = new();
    
    // Configuration
    private readonly int _maxFileHandles;
    private readonly int _maxProcessHandles;
    private readonly int _maxHandleAgeSeconds;
    
    // Tracking state
    private readonly ConcurrentDictionary<IntPtr, FileHandleInfo> _fileHandles = new();
    private readonly ConcurrentDictionary<int, ProcessHandleInfo> _processHandles = new();
    private int _totalFileHandlesCreated;
    private int _totalProcessHandlesCreated;
    private bool _disposed;
    
    // Events
    public event EventHandler<HandleLeakEventArgs>? HandleLeakDetected;
    public event EventHandler? CriticalHandleLeak;
    
    /// <summary>Current number of active file handles</summary>
    public int ActiveFileHandleCount => _fileHandles.Count;
    
    /// <summary>Current number of active process handles</summary>
    public int ActiveProcessHandleCount => _processHandles.Count;
    
    /// <summary>Total file handles created since monitor started</summary>
    public int TotalFileHandlesCreated => _totalFileHandlesCreated;
    
    /// <summary>Total process handles created since monitor started</summary>
    public int TotalProcessHandlesCreated => _totalProcessHandlesCreated;
    
    public HandleMonitor(
        string extensionId,
        ExtensionLogger? logger = null,
        int maxFileHandles = 100,
        int maxProcessHandles = 10,
        int maxHandleAgeSeconds = 300,
        int monitorIntervalMs = 5000)
    {
        _extensionId = extensionId;
        _logger = logger;
        _maxFileHandles = maxFileHandles;
        _maxProcessHandles = maxProcessHandles;
        _maxHandleAgeSeconds = maxHandleAgeSeconds;
        
        _monitorTimer = new Timer(
            callback: _ => MonitorHandles(),
            state: null,
            dueTime: monitorIntervalMs,
            period: monitorIntervalMs);
    }
    
    /// <summary>
    /// Tracks a file handle being opened.
    /// </summary>
    public void TrackFileHandle(IntPtr handle, string filePath, FileAccess access)
    {
        if (_disposed)
            return;
        
        var info = new FileHandleInfo
        {
            Handle = handle,
            FilePath = filePath,
            Access = access,
            OpenedAt = DateTime.UtcNow,
            StackTrace = Environment.StackTrace
        };
        
        _fileHandles[handle] = info;
        Interlocked.Increment(ref _totalFileHandlesCreated);
        
        // Check if we're exceeding limits
        if (_fileHandles.Count > _maxFileHandles)
        {
            _logger?.Warn($"[{_extensionId}] File handle count exceeded limit: {_fileHandles.Count} > {_maxFileHandles}");
            OnHandleLeakDetected(HandleType.File, _fileHandles.Count, _maxFileHandles);
        }
    }
    
    /// <summary>
    /// Tracks a file handle being closed.
    /// </summary>
    public void UntrackFileHandle(IntPtr handle)
    {
        if (_disposed)
            return;
        
        _fileHandles.TryRemove(handle, out _);
    }
    
    /// <summary>
    /// Tracks a process handle being opened.
    /// </summary>
    public void TrackProcessHandle(int processId, string processName)
    {
        if (_disposed)
            return;
        
        var info = new ProcessHandleInfo
        {
            ProcessId = processId,
            ProcessName = processName,
            OpenedAt = DateTime.UtcNow,
            StackTrace = Environment.StackTrace
        };
        
        _processHandles[processId] = info;
        Interlocked.Increment(ref _totalProcessHandlesCreated);
        
        // Check if we're exceeding limits
        if (_processHandles.Count > _maxProcessHandles)
        {
            _logger?.Warn($"[{_extensionId}] Process handle count exceeded limit: {_processHandles.Count} > {_maxProcessHandles}");
            OnHandleLeakDetected(HandleType.Process, _processHandles.Count, _maxProcessHandles);
        }
    }
    
    /// <summary>
    /// Tracks a process handle being closed.
    /// </summary>
    public void UntrackProcessHandle(int processId)
    {
        if (_disposed)
            return;
        
        _processHandles.TryRemove(processId, out _);
    }
    
    /// <summary>
    /// Gets information about all active file handles.
    /// </summary>
    public IReadOnlyList<FileHandleInfo> GetActiveFileHandles()
    {
        return _fileHandles.Values.ToList().AsReadOnly();
    }
    
    /// <summary>
    /// Gets information about all active process handles.
    /// </summary>
    public IReadOnlyList<ProcessHandleInfo> GetActiveProcessHandles()
    {
        return _processHandles.Values.ToList().AsReadOnly();
    }
    
    /// <summary>
    /// Forces closure of all tracked file handles.
    /// </summary>
    public void ForceCloseAllFileHandles()
    {
        foreach (var handle in _fileHandles.Keys.ToList())
        {
            try
            {
                // Note: Actual handle closure should be done by the owner
                // This just removes from tracking
                _fileHandles.TryRemove(handle, out _);
                _logger?.Warn($"[{_extensionId}] Marked file handle {handle} for closure");
            }
            catch
            {
                // Ignore errors during forced closure
            }
        }
    }
    
    /// <summary>
    /// Forces termination of all tracked processes.
    /// </summary>
    public void ForceTerminateAllProcesses()
    {
        foreach (var processId in _processHandles.Keys.ToList())
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.Kill();
                    _logger?.Warn($"[{_extensionId}] Terminated process {processId} ({_processHandles[processId].ProcessName})");
                }
                _processHandles.TryRemove(processId, out _);
            }
            catch
            {
                // Process may have already exited or be inaccessible
                _processHandles.TryRemove(processId, out _);
            }
        }
    }
    
    private void MonitorHandles()
    {
        if (_disposed)
            return;
        
        lock (_lock)
        {
            try
            {
                var now = DateTime.UtcNow;
                
                // Check for old file handles
                var oldFileHandles = _fileHandles.Values
                    .Where(h => (now - h.OpenedAt).TotalSeconds > _maxHandleAgeSeconds)
                    .ToList();
                
                foreach (var handle in oldFileHandles)
                {
                    _logger?.Warn($"[{_extensionId}] Potentially leaked file handle: {handle.FilePath} (opened {(now - handle.OpenedAt).TotalSeconds:F0}s ago)");
                    OnHandleLeakDetected(HandleType.File, _fileHandles.Count, _maxFileHandles);
                }
                
                // Check for old process handles
                var oldProcessHandles = _processHandles.Values
                    .Where(h => (now - h.OpenedAt).TotalSeconds > _maxHandleAgeSeconds)
                    .ToList();
                
                foreach (var handle in oldProcessHandles)
                {
                    _logger?.Warn($"[{_extensionId}] Potentially leaked process handle: {handle.ProcessName} (PID: {handle.ProcessId}, opened {(now - handle.OpenedAt).TotalSeconds:F0}s ago)");
                    OnHandleLeakDetected(HandleType.Process, _processHandles.Count, _maxProcessHandles);
                }
                
                // Critical check: if we have too many handles, trigger critical event
                if (_fileHandles.Count > _maxFileHandles * 2 || _processHandles.Count > _maxProcessHandles * 2)
                {
                    _logger?.Error($"[{_extensionId}] Critical handle leak detected!");
                    CriticalHandleLeak?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"[{_extensionId}] Error in handle monitor: {ex.Message}");
            }
        }
    }
    
    private void OnHandleLeakDetected(HandleType type, int currentCount, int maxCount)
    {
        HandleLeakDetected?.Invoke(this, new HandleLeakEventArgs
        {
            ExtensionId = _extensionId,
            HandleType = type,
            CurrentCount = currentCount,
            MaxCount = maxCount
        });
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        _monitorTimer.Dispose();
        
        // Log final statistics
        _logger?.Info($"[{_extensionId}] Handle monitor disposed. Total file handles: {_totalFileHandlesCreated}, Total process handles: {_totalProcessHandlesCreated}");
    }
    
    /// <summary>Handle types</summary>
    public enum HandleType
    {
        File,
        Process
    }
    
    /// <summary>Handle leak event arguments</summary>
    public sealed class HandleLeakEventArgs : EventArgs
    {
        public required string ExtensionId { get; init; }
        public required HandleType HandleType { get; init; }
        public required int CurrentCount { get; init; }
        public required int MaxCount { get; init; }
    }
    
    /// <summary>File handle information</summary>
    public sealed class FileHandleInfo
    {
        public IntPtr Handle { get; init; }
        public required string FilePath { get; init; }
        public FileAccess Access { get; init; }
        public DateTime OpenedAt { get; init; }
        public string? StackTrace { get; init; }
    }
    
    /// <summary>Process handle information</summary>
    public sealed class ProcessHandleInfo
    {
        public int ProcessId { get; init; }
        public required string ProcessName { get; init; }
        public DateTime OpenedAt { get; init; }
        public string? StackTrace { get; init; }
    }
}
