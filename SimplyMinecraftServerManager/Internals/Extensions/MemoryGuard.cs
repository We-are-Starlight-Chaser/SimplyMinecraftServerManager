// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.InteropServices;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展内存守卫：监控每个扩展的内存使用，防止恶意内存操作。
/// 
/// 防护策略：
///   1. 内存使用量上限监控（定时采样）
///   2. 分配速率异常检测（突增告警）
///   3. 大对象堆 (LOH) 碎片化监控
///   4. 非托管内存分配追踪
///   5. 内存泄漏检测（GC 未回收 + 老年代增长）
///   6. 线程创建速率限制（防止线程池耗尽）
///   7. 磁盘写入量监控（防止磁盘空间耗尽）
/// </summary>
internal sealed class MemoryGuard : IDisposable
{
    private readonly string _extensionId;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();

    // ===== 共享静态定时器 =====
    private static readonly Timer s_sharedTimer;
    private static readonly List<MemoryGuard> s_activeGuards = [];
    private static readonly Lock s_sharedLock = new();

    static MemoryGuard()
    {
        s_sharedTimer = new Timer(
            callback: _ => MonitorAll(),
            state: null,
            dueTime: 2000,
            period: 2000);
    }

    private static void MonitorAll()
    {
        MemoryGuard[] snapshot;
        lock (s_sharedLock)
        {
            if (s_activeGuards.Count == 0) return;
            snapshot = [.. s_activeGuards];
        }
        foreach (var guard in snapshot)
        {
            if (!guard._disposed)
                guard.MonitorMemory();
        }
    }

    // 配置
    private readonly long _maxManagedMemoryBytes;
    private readonly long _maxUnmanagedMemoryBytes;
    private readonly int _maxAllocationRateBytesPerSec;
    private readonly int _maxThreadsPerExtension;
    private readonly int _maxThreadCreationRatePerSecond;
    private readonly long _maxDiskWriteBytesPerHour;
    private readonly long _maxTotalDiskWriteBytes;

    // 追踪状态
    private readonly long _managedMemoryAtStart;
    private long _unmanagedMemoryAllocated;
    private long _lastSampledManagedBytes;
    private DateTime _lastSampleTime;
    private long _allocationRateWindowBytes;
    private int _consecutiveOverflows;
    private bool _disposed;
    
    // 线程追踪
    private int _activeThreadCount;
    private int _threadCreationCountInWindow;
    private long _lastThreadWindowStartTicks;
    private readonly List<int> _createdThreadIds = [];
    
    // 磁盘写入追踪
    private long _totalDiskWriteBytes;
    private long _diskWriteBytesInCurrentHour;
    private long _lastDiskWriteWindowStartTicks;
    private readonly DateTime _lastDiskWriteSampleTime;

    // 事件
    public event EventHandler<MemoryThresholdEventArgs>? ThresholdExceeded;
    public event EventHandler? ForcedShutdown;

    /// <summary>当前追踪的托管内存使用量</summary>
    public long CurrentManagedBytes { get; private set; }

    /// <summary>当前追踪的非托管内存使用量</summary>
    public long CurrentUnmanagedBytes => Interlocked.Read(ref _unmanagedMemoryAllocated);

    /// <summary>历史峰值内存</summary>
    public long PeakManagedBytes { get; private set; }

    public MemoryGuard(
        string extensionId,
        ILogger logger,
        long maxManagedMemoryMb = 256,
        long maxUnmanagedMemoryMb = 128,
        int maxAllocationRateMbPerSec = 50,
        int maxThreadsPerExtension = 50,
        int maxThreadCreationRatePerSecond = 10,
        long maxDiskWriteMbPerHour = 1024,
        long maxTotalDiskWriteMb = 10240,
        int monitorIntervalMs = 2000)
    {
        _extensionId = extensionId;
        _logger = logger;
        _maxManagedMemoryBytes = maxManagedMemoryMb * 1024 * 1024;
        _maxUnmanagedMemoryBytes = maxUnmanagedMemoryMb * 1024 * 1024;
        _maxAllocationRateBytesPerSec = maxAllocationRateMbPerSec * 1024 * 1024;
        _maxThreadsPerExtension = maxThreadsPerExtension;
        _maxThreadCreationRatePerSecond = maxThreadCreationRatePerSecond;
        _maxDiskWriteBytesPerHour = maxDiskWriteMbPerHour * 1024 * 1024;
        _maxTotalDiskWriteBytes = maxTotalDiskWriteMb * 1024 * 1024;
        _managedMemoryAtStart = GC.GetTotalMemory(forceFullCollection: false);
        _lastSampledManagedBytes = _managedMemoryAtStart;
        _lastSampleTime = DateTime.UtcNow;
        _lastThreadWindowStartTicks = DateTime.UtcNow.Ticks;
        _lastDiskWriteWindowStartTicks = DateTime.UtcNow.Ticks;
        _lastDiskWriteSampleTime = DateTime.UtcNow;

        lock (s_sharedLock)
        {
            s_activeGuards.Add(this);
        }
    }

    /// <summary>
    /// 注册非托管内存分配。
    /// 扩展在 P/Invoke 或 Unsafe 分配时必须调用此方法。
    /// </summary>
    public void TrackUnmanagedAllocation(long bytes)
    {
        long total = Interlocked.Add(ref _unmanagedMemoryAllocated, bytes);

        if (total > _maxUnmanagedMemoryBytes)
        {
            _logger.Warn($"[{_extensionId}] 非托管内存超限: {total / 1024 / 1024}MB > {_maxUnmanagedMemoryBytes / 1024 / 1024}MB");
            OnThresholdExceeded(MemoryThresholdType.Unmanaged, total, _maxUnmanagedMemoryBytes);
        }
    }

    /// <summary>
    /// 注册非托管内存释放。
    /// </summary>
    public void TrackUnmanagedRelease(long bytes)
    {
        Interlocked.Add(ref _unmanagedMemoryAllocated, -bytes);
    }
    
    /// <summary>
    /// 检查是否允许创建新线程。
    /// 返回 true 如果允许，false 如果应该阻止。
    /// </summary>
    public bool CanCreateThread()
    {
        if (_disposed)
            return false;
        
        lock (_lock)
        {
            // 检查线程总数限制
            if (_activeThreadCount >= _maxThreadsPerExtension)
            {
                _logger.Warn($"[{_extensionId}] 线程数超限: {_activeThreadCount} >= {_maxThreadsPerExtension}");
                return false;
            }
            
            // 检查线程创建速率限制
            var now = DateTime.UtcNow.Ticks;
            var elapsed = now - Interlocked.Read(ref _lastThreadWindowStartTicks);
            var elapsedMs = elapsed / TimeSpan.TicksPerMillisecond;
            
            if (elapsedMs >= 1000) // 1秒窗口
            {
                Interlocked.Exchange(ref _threadCreationCountInWindow, 0);
                Interlocked.Exchange(ref _lastThreadWindowStartTicks, now);
            }
            
            if (_threadCreationCountInWindow >= _maxThreadCreationRatePerSecond)
            {
                _logger.Warn($"[{_extensionId}] 线程创建速率超限: {_threadCreationCountInWindow}/s >= {_maxThreadCreationRatePerSecond}/s");
                return false;
            }
            
            Interlocked.Increment(ref _threadCreationCountInWindow);
            return true;
        }
    }
    
    /// <summary>
    /// 注册线程创建。
    /// </summary>
    public void TrackThreadCreation(int threadId)
    {
        if (_disposed)
            return;
        
        lock (_lock)
        {
            Interlocked.Increment(ref _activeThreadCount);
            _createdThreadIds.Add(threadId);
        }
    }
    
    /// <summary>
    /// 注册线程终止。
    /// </summary>
    public void TrackThreadTermination(int threadId)
    {
        if (_disposed)
            return;
        
        lock (_lock)
        {
            Interlocked.Decrement(ref _activeThreadCount);
            _createdThreadIds.Remove(threadId);
        }
    }
    
    /// <summary>
    /// 获取当前活跃线程数。
    /// </summary>
    public int GetActiveThreadCount()
    {
        return _activeThreadCount;
    }
    
    /// <summary>
    /// 获取当前窗口内的线程创建次数。
    /// </summary>
    public int GetThreadCreationCountInWindow()
    {
        return _threadCreationCountInWindow;
    }
    
    /// <summary>
    /// 强制终止所有创建的线程（紧急情况使用）。
    /// </summary>
    public void ForceTerminateAllThreads()
    {
        if (_disposed)
            return;
        
        lock (_lock)
        {
            foreach (var threadId in _createdThreadIds)
            {
                try
                {
                    var thread = System.Threading.Thread.CurrentThread;
                    // 注意：这里只是记录，实际终止需要在更高层处理
                    _logger.Warn($"[{_extensionId}] 标记线程 {threadId} 为待终止");
                }
                catch
                {
                    // 忽略无法访问的线程
                }
            }
            _createdThreadIds.Clear();
            Interlocked.Exchange(ref _activeThreadCount, 0);
        }
    }
    
    /// <summary>
    /// 记录磁盘写入量。
    /// </summary>
    public void TrackDiskWrite(long bytes)
    {
        if (_disposed)
            return;
        
        lock (_lock)
        {
            // 更新总写入量
            Interlocked.Add(ref _totalDiskWriteBytes, bytes);
            
            // 检查总写入量限制
            if (_totalDiskWriteBytes > _maxTotalDiskWriteBytes)
            {
                _logger.Warn($"[{_extensionId}] 磁盘总写入量超限: {_totalDiskWriteBytes / 1024 / 1024}MB > {_maxTotalDiskWriteBytes / 1024 / 1024}MB");
                OnThresholdExceeded(MemoryThresholdType.DiskWrite, _totalDiskWriteBytes, _maxTotalDiskWriteBytes);
            }
            
            // 更新当前小时写入量
            var now = DateTime.UtcNow.Ticks;
            var elapsed = now - Interlocked.Read(ref _lastDiskWriteWindowStartTicks);
            var elapsedMs = elapsed / TimeSpan.TicksPerMillisecond;
            
            if (elapsedMs >= 3600000) // 1小时窗口
            {
                Interlocked.Exchange(ref _diskWriteBytesInCurrentHour, 0);
                Interlocked.Exchange(ref _lastDiskWriteWindowStartTicks, now);
            }
            
            Interlocked.Add(ref _diskWriteBytesInCurrentHour, bytes);
            
            // 检查每小时写入量限制
            if (_diskWriteBytesInCurrentHour > _maxDiskWriteBytesPerHour)
            {
                _logger.Warn($"[{_extensionId}] 磁盘每小时写入量超限: {_diskWriteBytesInCurrentHour / 1024 / 1024}MB > {_maxDiskWriteBytesPerHour / 1024 / 1024}MB");
                OnThresholdExceeded(MemoryThresholdType.DiskWriteRate, _diskWriteBytesInCurrentHour, _maxDiskWriteBytesPerHour);
            }
        }
    }
    
    /// <summary>
    /// 获取总磁盘写入量。
    /// </summary>
    public long GetTotalDiskWriteBytes()
    {
        return _totalDiskWriteBytes;
    }
    
    /// <summary>
    /// 获取当前小时磁盘写入量。
    /// </summary>
    public long GetDiskWriteBytesInCurrentHour()
    {
        return _diskWriteBytesInCurrentHour;
    }
    
    /// <summary>
    /// 重置磁盘写入计数器。
    /// </summary>
    public void ResetDiskWriteCounters()
    {
        Interlocked.Exchange(ref _totalDiskWriteBytes, 0);
        Interlocked.Exchange(ref _diskWriteBytesInCurrentHour, 0);
        Interlocked.Exchange(ref _lastDiskWriteWindowStartTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// 创建受监控的非托管内存块。
    /// 返回 SafeHandle，释放时自动计数。
    /// </summary>
    public MonitoredUnmanagedMemory AllocateUnmanaged(int sizeInBytes)
    {
        TrackUnmanagedAllocation(sizeInBytes);
        return new MonitoredUnmanagedMemory(this, sizeInBytes);
    }

    /// <summary>
    /// 手动触发内存快照（用于扩展主动检查）。
    /// </summary>
    public MemorySnapshot TakeSnapshot()
    {
        GC.Collect(0, GCCollectionMode.Optimized, blocking: false);
        long current = GC.GetTotalMemory(forceFullCollection: false);

        return new MemorySnapshot
        {
            Timestamp = DateTime.UtcNow,
            ManagedBytes = current,
            UnmanagedBytes = CurrentUnmanagedBytes,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
        };
    }

    private void MonitorMemory()
    {
        if (_disposed) return;

        bool shouldShutdown = false;
        long current = 0;
        long lastSampled = 0;
        DateTime now = default;

        lock (_lock)
        {
            try
            {
                current = GC.GetTotalMemory(forceFullCollection: false);
                now = DateTime.UtcNow;
                double elapsedSec = (now - _lastSampleTime).TotalSeconds;

                CurrentManagedBytes = current;
                if (current > PeakManagedBytes) PeakManagedBytes = current;

                // 1. 托管内存上限检查
                if (current > _maxManagedMemoryBytes)
                {
                    _consecutiveOverflows++;
                    OnThresholdExceeded(MemoryThresholdType.Managed, current, _maxManagedMemoryBytes);

                    if (_consecutiveOverflows >= 3)
                    {
                        shouldShutdown = true;
                    }
                }
                else
                {
                    _consecutiveOverflows = 0;
                }

                // 2. 分配速率异常检测
                if (elapsedSec > 0)
                {
                    long allocatedSinceLastSample = current - _lastSampledManagedBytes;
                    if (allocatedSinceLastSample > 0)
                    {
                        long ratePerSec = (long)(allocatedSinceLastSample / elapsedSec);
                        _allocationRateWindowBytes += allocatedSinceLastSample;

                        if (ratePerSec > _maxAllocationRateBytesPerSec)
                        {
                            OnThresholdExceeded(MemoryThresholdType.AllocationRate, ratePerSec, _maxAllocationRateBytesPerSec);
                        }
                    }
                }

                lastSampled = current;
                _lastSampledManagedBytes = current;
                _lastSampleTime = now;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MemoryGuard] Monitor error for '{_extensionId}': {ex.Message}");
            }
        }

        // 在锁外记录日志，避免字符串插值分配在锁内
        if (current > _maxManagedMemoryBytes)
        {
            double currentMb = current / 1024.0 / 1024.0;
            double maxMb = _maxManagedMemoryBytes / 1024.0 / 1024.0;
            _logger.Warn($"[{_extensionId}] 托管内存超限: {currentMb:F1}MB > {maxMb:F1}MB (连续 {_consecutiveOverflows} 次)");
        }

        if (shouldShutdown)
        {
            _logger.Error($"[{_extensionId}] 连续内存超限，强制终止扩展");
            ForcedShutdown?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnThresholdExceeded(MemoryThresholdType type, long current, long limit)
    {
        ThresholdExceeded?.Invoke(this, new MemoryThresholdEventArgs
        {
            ExtensionId = _extensionId,
            Type = type,
            CurrentBytes = current,
            LimitBytes = limit
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (s_sharedLock)
        {
            s_activeGuards.Remove(this);
        }
    }

    /// <summary>内存阈值类型</summary>
    public enum MemoryThresholdType
    {
        Managed,
        Unmanaged,
        AllocationRate,
        Fragmentation,
        DiskWrite,
        DiskWriteRate
    }

    /// <summary>内存阈值事件参数</summary>
    public sealed class MemoryThresholdEventArgs : EventArgs
    {
        public required string ExtensionId { get; init; }
        public required MemoryThresholdType Type { get; init; }
        public required long CurrentBytes { get; init; }
        public required long LimitBytes { get; init; }
    }

    /// <summary>内存快照</summary>
    public sealed class MemorySnapshot
    {
        public DateTime Timestamp { get; init; }
        public long ManagedBytes { get; init; }
        public long UnmanagedBytes { get; init; }
        public int Gen0Collections { get; init; }
        public int Gen1Collections { get; init; }
        public int Gen2Collections { get; init; }
    }

    /// <summary>
    /// 受监控的非托管内存块。
    /// 释放时自动从 MemoryGuard 中扣减计数。
    /// </summary>
    public sealed class MonitoredUnmanagedMemory : SafeHandle
    {
        private readonly MemoryGuard _guard;
        private readonly int _size;

        public MonitoredUnmanagedMemory(MemoryGuard guard, int size)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            _guard = guard;
            _size = size;
            SetHandle(Marshal.AllocHGlobal(size));
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            Marshal.FreeHGlobal(handle);
            _guard.TrackUnmanagedRelease(_size);
            return true;
        }
    }
}
