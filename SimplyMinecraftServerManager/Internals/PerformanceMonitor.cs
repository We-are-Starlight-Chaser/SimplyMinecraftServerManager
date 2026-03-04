using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 服务器性能监控数据。
    /// </summary>
    public class PerformanceData
    {
        /// <summary>进程使用的内存（MB）</summary>
        public double MemoryUsageMb { get; set; }

        /// <summary>CPU 使用率（0-100%）</summary>
        public double CpuUsage { get; set; }

        /// <summary>总存储空间使用（MB）</summary>
        public long TotalStorageMb { get; set; }

        /// <summary>世界文件夹大小（MB）</summary>
        public Dictionary<string, long> WorldStorageMb { get; set; } = new();

        /// <summary>数据更新时间</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 服务器性能监控器，用于监控运行中的服务器进程。
    /// </summary>
    public class PerformanceMonitor : IDisposable
    {
        private readonly string _instanceId;
        private Timer? _monitorTimer;
        private PerformanceCounter? _cpuCounter;
        private Process? _targetProcess;
        private DateTime _lastCpuTime = DateTime.MinValue;
        private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;

        public string InstanceId => _instanceId;

        /// <summary>
        /// 当性能数据更新时触发。
        /// </summary>
        public event EventHandler<PerformanceData>? DataUpdated;

        public PerformanceMonitor(string instanceId)
        {
            _instanceId = instanceId;
        }

        /// <summary>
        /// 开始监控。
        /// </summary>
        public void Start(int intervalMs = 2000)
        {
            Stop();

            // 获取进程
            var serverProcess = ServerProcessManager.GetProcess(_instanceId);
            if (serverProcess?.ProcessId == null)
                throw new InvalidOperationException("Server process not found");

            try
            {
                _targetProcess = Process.GetProcessById(serverProcess.ProcessId.Value);

                // 创建 CPU 性能计数器
                try
                {
                    _cpuCounter = new PerformanceCounter(
                        "Process",
                        "% Processor Time",
                        _targetProcess.ProcessName,
                        true);
                    // 预热计数器
                    _cpuCounter.NextValue();
                }
                catch
                {
                    _cpuCounter = null;
                }

                _lastCpuTime = DateTime.Now;
                _lastTotalProcessorTime = _targetProcess.TotalProcessorTime;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize performance monitoring: {ex.Message}");
            }

            // 启动定时器
            _monitorTimer = new Timer(_ => CollectData(), null, 0, intervalMs);
        }

        /// <summary>
        /// 停止监控。
        /// </summary>
        public void Stop()
        {
            _monitorTimer?.Dispose();
            _monitorTimer = null;
            _cpuCounter?.Dispose();
            _cpuCounter = null;
            _targetProcess = null;
        }

        private void CollectData()
        {
            if (_targetProcess == null) return;

            try
            {
                // 刷新进程信息
                _targetProcess.Refresh();

                // 获取内存使用（MB）
                double memoryMb = _targetProcess.WorkingSet64 / (1024.0 * 1024.0);

                // 获取 CPU 使用率
                double cpuUsage = 0;
                try
                {
                    if (_cpuCounter != null)
                    {
                        cpuUsage = _cpuCounter.NextValue();
                    }
                    else
                    {
                        // 备用方法：计算 CPU 使用率
                        var now = DateTime.Now;
                        var totalProcessorTime = _targetProcess.TotalProcessorTime;
                        var timeDiff = (now - _lastCpuTime).TotalMilliseconds;
                        var cpuDiff = (totalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;

                        if (timeDiff > 0)
                        {
                            // 除以处理器数量得到单核百分比
                            cpuUsage = (cpuDiff / timeDiff / Environment.ProcessorCount) * 100;
                        }

                        _lastCpuTime = now;
                        _lastTotalProcessorTime = totalProcessorTime;
                    }
                }
                catch { }

                // 限制 CPU 显示范围
                cpuUsage = Math.Clamp(cpuUsage, 0, 100 * Environment.ProcessorCount);

                // 获取存储空间使用
                var storageData = GetStorageUsage();

                var data = new PerformanceData
                {
                    MemoryUsageMb = memoryMb,
                    CpuUsage = cpuUsage,
                    TotalStorageMb = storageData.TotalMb,
                    WorldStorageMb = storageData.WorldSizes,
                    Timestamp = DateTime.Now
                };

                DataUpdated?.Invoke(this, data);
            }
            catch (Exception)
            {
                // 忽略监控过程中的错误
            }
        }

        private (long TotalMb, Dictionary<string, long> WorldSizes) GetStorageUsage()
        {
            var worldSizes = new Dictionary<string, long>();
            long totalBytes = 0;

            try
            {
                string instanceDir = PathHelper.GetInstanceDir(_instanceId);

                if (Directory.Exists(instanceDir))
                {
                    // 计算总大小
                    totalBytes = GetDirectorySize(instanceDir);

                    // 获取各个世界的大小
                    string[] worldFolders = new[] { "world", "world_nether", "world_the_end" };

                    foreach (var worldName in worldFolders)
                    {
                        string worldPath = Path.Combine(instanceDir, worldName);
                        if (Directory.Exists(worldPath))
                        {
                            long sizeBytes = GetDirectorySize(worldPath);
                            worldSizes[worldName] = sizeBytes / (1024 * 1024);
                        }
                    }
                }
            }
            catch { }

            return (totalBytes / (1024 * 1024), worldSizes);
        }

        private long GetDirectorySize(string path)
        {
            long size = 0;

            try
            {
                // 获取文件大小
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Exists)
                            size += fi.Length;
                    }
                    catch { }
                }
            }
            catch { }

            return size;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    /// <summary>
    /// 全局性能监控管理器。
    /// </summary>
    public static class PerformanceMonitorManager
    {
        private static readonly Dictionary<string, PerformanceMonitor> _monitors = new();

        /// <summary>
        /// 获取或创建指定实例的性能监控器。
        /// </summary>
        public static PerformanceMonitor GetOrCreate(string instanceId)
        {
            if (!_monitors.TryGetValue(instanceId, out var monitor))
            {
                monitor = new PerformanceMonitor(instanceId);
                _monitors[instanceId] = monitor;
            }
            return monitor;
        }

        /// <summary>
        /// 移除并停止指定实例的监控器。
        /// </summary>
        public static void Remove(string instanceId)
        {
            if (_monitors.TryGetValue(instanceId, out var monitor))
            {
                monitor.Dispose();
                _monitors.Remove(instanceId);
            }
        }

        /// <summary>
        /// 获取指定实例的监控器（如果存在）。
        /// </summary>
        public static PerformanceMonitor? Get(string instanceId)
        {
            _monitors.TryGetValue(instanceId, out var monitor);
            return monitor;
        }

        /// <summary>
        /// 停止所有监控。
        /// </summary>
        public static void StopAll()
        {
            foreach (var monitor in _monitors.Values)
            {
                monitor.Dispose();
            }
            _monitors.Clear();
        }
    }
}
