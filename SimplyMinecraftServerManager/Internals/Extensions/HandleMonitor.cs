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
/// 用于跟踪扩展中文件和进程句柄泄漏的监视器。
/// 检测扩展未正确释放句柄的情况。
/// </summary>
internal sealed class HandleMonitor : IDisposable
{
    private readonly string _extensionId;
    private readonly ExtensionLogger? _logger;
    private readonly Timer _monitorTimer;
    private readonly Lock _lock = new();
    
    // 配置
    private readonly int _maxFileHandles;
    private readonly int _maxProcessHandles;
    private readonly int _maxHandleAgeSeconds;
    
    // 跟踪状态
    private readonly ConcurrentDictionary<IntPtr, FileHandleInfo> _fileHandles = new();
    private readonly ConcurrentDictionary<int, ProcessHandleInfo> _processHandles = new();
    private int _totalFileHandlesCreated;
    private int _totalProcessHandlesCreated;
    private bool _disposed;
    
    // 事件
    public event EventHandler<HandleLeakEventArgs>? HandleLeakDetected;
    public event EventHandler? CriticalHandleLeak;
    
    /// <summary>当前活动文件句柄数量</summary>
    public int ActiveFileHandleCount => _fileHandles.Count;
    
    /// <summary>当前活动进程句柄数量</summary>
    public int ActiveProcessHandleCount => _processHandles.Count;
    
    /// <summary>监视器启动以来创建的文件句柄总数</summary>
    public int TotalFileHandlesCreated => _totalFileHandlesCreated;
    
    /// <summary>监视器启动以来创建的进程句柄总数</summary>
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
    /// 跟踪打开的文件句柄。
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
            StackTrace = CaptureStackTrace()
        };
        
        _fileHandles[handle] = info;
        Interlocked.Increment(ref _totalFileHandlesCreated);
        
        // 检查是否超过限制
        if (_fileHandles.Count > _maxFileHandles)
        {
            _logger?.Warn($"[{_extensionId}] 文件句柄数量超过限制: {_fileHandles.Count} > {_maxFileHandles}");
            OnHandleLeakDetected(HandleType.File, _fileHandles.Count, _maxFileHandles);
        }
    }
    
    /// <summary>
    /// 跟踪关闭的文件句柄。
    /// </summary>
    public void UntrackFileHandle(IntPtr handle)
    {
        if (_disposed)
            return;
        
        _fileHandles.TryRemove(handle, out _);
    }
    
    /// <summary>
    /// 跟踪打开的进程句柄。
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
            StackTrace = CaptureStackTrace()
        };
        
        _processHandles[processId] = info;
        Interlocked.Increment(ref _totalProcessHandlesCreated);
        
        // 检查是否超过限制
        if (_processHandles.Count > _maxProcessHandles)
        {
            _logger?.Warn($"[{_extensionId}] 进程句柄数量超过限制: {_processHandles.Count} > {_maxProcessHandles}");
            OnHandleLeakDetected(HandleType.Process, _processHandles.Count, _maxProcessHandles);
        }
    }
    
    /// <summary>
    /// 跟踪关闭的进程句柄。
    /// </summary>
    public void UntrackProcessHandle(int processId)
    {
        if (_disposed)
            return;
        
        _processHandles.TryRemove(processId, out _);
    }
    
    /// <summary>
    /// 获取所有活动文件句柄的信息。
    /// </summary>
    public IReadOnlyList<FileHandleInfo> GetActiveFileHandles()
    {
        return _fileHandles.Values.ToList().AsReadOnly();
    }
    
    /// <summary>
    /// 获取所有活动进程句柄的信息。
    /// </summary>
    public IReadOnlyList<ProcessHandleInfo> GetActiveProcessHandles()
    {
        return _processHandles.Values.ToList().AsReadOnly();
    }
    
    /// <summary>
    /// 强制关闭所有跟踪的文件句柄。
    /// </summary>
    public void ForceCloseAllFileHandles()
    {
        foreach (var handle in _fileHandles.Keys.ToList())
        {
            try
            {
                // 注意：实际句柄关闭应由所有者完成
                // 这里只是从跟踪中移除
                _fileHandles.TryRemove(handle, out _);
                _logger?.Warn($"[{_extensionId}] 已标记文件句柄 {handle} 待关闭");
            }
            catch
            {
                // 忽略强制关闭期间的错误
            }
        }
    }
    
    /// <summary>
    /// 强制终止所有跟踪的进程。
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
                    _logger?.Warn($"[{_extensionId}] 已终止进程 {processId} ({_processHandles[processId].ProcessName})");
                }
                _processHandles.TryRemove(processId, out _);
            }
            catch
            {
                // 进程可能已退出或无法访问
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
                
                // 检查旧文件句柄
                var oldFileHandles = _fileHandles.Values
                    .Where(h => (now - h.OpenedAt).TotalSeconds > _maxHandleAgeSeconds)
                    .ToList();
                
                foreach (var handle in oldFileHandles)
                {
                    _logger?.Warn($"[{_extensionId}] 可能泄漏的文件句柄: {handle.FilePath} (已打开 {(now - handle.OpenedAt).TotalSeconds:F0}秒)");
                    OnHandleLeakDetected(HandleType.File, _fileHandles.Count, _maxFileHandles);
                }
                
                // 检查旧进程句柄
                var oldProcessHandles = _processHandles.Values
                    .Where(h => (now - h.OpenedAt).TotalSeconds > _maxHandleAgeSeconds)
                    .ToList();
                
                foreach (var handle in oldProcessHandles)
                {
                    _logger?.Warn($"[{_extensionId}] 可能泄漏的进程句柄: {handle.ProcessName} (PID: {handle.ProcessId}, 已打开 {(now - handle.OpenedAt).TotalSeconds:F0}秒)");
                    OnHandleLeakDetected(HandleType.Process, _processHandles.Count, _maxProcessHandles);
                }
                
                // 严重检查：如果句柄过多，触发严重事件
                if (_fileHandles.Count > _maxFileHandles * 2 || _processHandles.Count > _maxProcessHandles * 2)
                {
                    _logger?.Error($"[{_extensionId}] 检测到严重句柄泄漏!");
                    CriticalHandleLeak?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"[{_extensionId}] 句柄监视器错误: {ex.Message}");
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

    /// <summary>
    /// 捕获精简堆栈信息（仅保留前 5 帧），降低内存开销。
    /// </summary>
    private static string? CaptureStackTrace()
    {
        try
        {
            var trace = new StackTrace(skipFrames: 2);
            var frames = trace.GetFrames();
            if (frames is null || frames.Length == 0)
                return null;

            int limit = Math.Min(frames.Length, 5);
            return string.Join(" <- ", frames.Take(limit)
                .Select(f => $"{f.GetMethod()?.DeclaringType?.Name}.{f.GetMethod()?.Name}"));
        }
        catch
        {
            return null;
        }
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        _monitorTimer.Dispose();
        
        // 记录最终统计信息
        _logger?.Info($"[{_extensionId}] 句柄监视器已释放。文件句柄总数: {_totalFileHandlesCreated}, 进程句柄总数: {_totalProcessHandlesCreated}");
    }
    
    /// <summary>句柄类型</summary>
    public enum HandleType
    {
        File,
        Process
    }
    
    /// <summary>句柄泄漏事件参数</summary>
    public sealed class HandleLeakEventArgs : EventArgs
    {
        public required string ExtensionId { get; init; }
        public required HandleType HandleType { get; init; }
        public required int CurrentCount { get; init; }
        public required int MaxCount { get; init; }
    }
    
    /// <summary>文件句柄信息</summary>
    public sealed class FileHandleInfo
    {
        public IntPtr Handle { get; init; }
        public required string FilePath { get; init; }
        public FileAccess Access { get; init; }
        public DateTime OpenedAt { get; init; }
        public string? StackTrace { get; init; }
    }
    
    /// <summary>进程句柄信息</summary>
    public sealed class ProcessHandleInfo
    {
        public int ProcessId { get; init; }
        public required string ProcessName { get; init; }
        public DateTime OpenedAt { get; init; }
        public string? StackTrace { get; init; }
    }
}
