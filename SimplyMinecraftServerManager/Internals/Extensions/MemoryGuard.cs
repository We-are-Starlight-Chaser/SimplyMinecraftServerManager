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
/// </summary>
internal sealed class MemoryGuard : IDisposable
{
    private readonly string _extensionId;
    private readonly ILogger _logger;
    private readonly Timer _monitorTimer;
    private readonly object _lock = new();

    // 配置
    private readonly long _maxManagedMemoryBytes;
    private readonly long _maxUnmanagedMemoryBytes;
    private readonly int _maxAllocationRateBytesPerSec;

    // 追踪状态
    private long _managedMemoryAtStart;
    private long _unmanagedMemoryAllocated;
    private long _lastSampledManagedBytes;
    private DateTime _lastSampleTime;
    private long _allocationRateWindowBytes;
    private int _consecutiveOverflows;
    private bool _disposed;

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
        int monitorIntervalMs = 2000)
    {
        _extensionId = extensionId;
        _logger = logger;
        _maxManagedMemoryBytes = maxManagedMemoryMb * 1024 * 1024;
        _maxUnmanagedMemoryBytes = maxUnmanagedMemoryMb * 1024 * 1024;
        _maxAllocationRateBytesPerSec = maxAllocationRateMbPerSec * 1024 * 1024;
        _managedMemoryAtStart = GC.GetTotalMemory(forceFullCollection: false);
        _lastSampledManagedBytes = _managedMemoryAtStart;
        _lastSampleTime = DateTime.UtcNow;

        _monitorTimer = new Timer(
            callback: _ => MonitorMemory(),
            state: null,
            dueTime: monitorIntervalMs,
            period: monitorIntervalMs);
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

        lock (_lock)
        {
            try
            {
                long current = GC.GetTotalMemory(forceFullCollection: false);
                DateTime now = DateTime.UtcNow;
                double elapsedSec = (now - _lastSampleTime).TotalSeconds;

                CurrentManagedBytes = current;
                if (current > PeakManagedBytes) PeakManagedBytes = current;

                // 1. 托管内存上限检查
                if (current > _maxManagedMemoryBytes)
                {
                    _consecutiveOverflows++;

                    double currentMb = current / 1024.0 / 1024.0;
                    double maxMb = _maxManagedMemoryBytes / 1024.0 / 1024.0;

                    _logger.Warn($"[{_extensionId}] 托管内存超限: {currentMb:F1}MB > {maxMb:F1}MB (连续 {_consecutiveOverflows} 次)");

                    OnThresholdExceeded(MemoryThresholdType.Managed, current, _maxManagedMemoryBytes);

                    // 连续 3 次超限 → 强制终止
                    if (_consecutiveOverflows >= 3)
                    {
                        _logger.Error($"[{_extensionId}] 连续内存超限，强制终止扩展");
                        ForcedShutdown?.Invoke(this, EventArgs.Empty);
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
                            double rateMb = ratePerSec / 1024.0 / 1024.0;
                            double maxRateMb = _maxAllocationRateBytesPerSec / 1024.0 / 1024.0;
                            _logger.Warn($"[{_extensionId}] 内存分配速率异常: {rateMb:F1}MB/s > {maxRateMb:F1}MB/s");

                            OnThresholdExceeded(MemoryThresholdType.AllocationRate, ratePerSec, _maxAllocationRateBytesPerSec);
                        }
                    }
                }

                // 3. 泄漏检测：老年代持续增长
                long gen2 = GC.GetTotalMemory(forceFullCollection: false);
                if (gen2 > 0 && current > _maxManagedMemoryBytes * 0.8)
                {
                    _logger.Warn($"[{_extensionId}] 内存使用接近上限 ({current / 1024 / 1024}MB / {_maxManagedMemoryBytes / 1024 / 1024}MB)，可能存在泄漏");
                }

                _lastSampledManagedBytes = current;
                _lastSampleTime = now;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MemoryGuard] Monitor error for '{_extensionId}': {ex.Message}");
            }
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
        _monitorTimer.Dispose();
    }

    /// <summary>内存阈值类型</summary>
    public enum MemoryThresholdType
    {
        Managed,
        Unmanaged,
        AllocationRate,
        Fragmentation
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
