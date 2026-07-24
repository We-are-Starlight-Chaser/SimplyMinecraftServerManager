// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

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
        public Dictionary<string, long> WorldStorageMb { get; set; } = [];

        /// <summary>数据更新时间</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 服务器性能监控器，用于监控运行中的服务器进程。
    /// </summary>
public class PerformanceMonitor(string instanceId) : IDisposable
    {
        private readonly string _instanceId = instanceId;
        private readonly Lock _lock = new();
        private Timer? _monitorTimer;
        private PerformanceCounter? _cpuCounter;
        private Process? _targetProcess;
        private DateTime _lastCpuTime = DateTime.MinValue;
        private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;
        private bool _disposed;

        private long _cachedStorageMb;
        private Dictionary<string, long> _cachedWorldSizes = [];
        private DateTime _lastStorageCacheTime = DateTime.MinValue;
        private readonly int _storageCacheIntervalMs = 30000;
        private readonly Lock _cacheLock = new();

        /// <summary>
        /// 当性能数据更新时触发。
        /// </summary>
        public event EventHandler<PerformanceData>? DataUpdated;

        /// <summary>
        /// 开始监控。
        /// </summary>
        public void Start(int intervalMs = 2000)
        {
            lock (_lock)
            {
                if (_disposed) return;

                // 停止已有的定时器和清理资源
                _monitorTimer?.Dispose();
                _monitorTimer = null;
                _cpuCounter?.Dispose();
                _cpuCounter = null;
                _targetProcess?.Dispose();
                _targetProcess = null;

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
                            "Processor",
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
        }

        /// <summary>
        /// 停止监控。
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _monitorTimer?.Dispose();
                _monitorTimer = null;
                _cpuCounter?.Dispose();
                _cpuCounter = null;
                _targetProcess?.Dispose();
                _targetProcess = null;
            }
        }

private void CollectData()
        {
            Process? process;
            PerformanceCounter? cpuCounter;
            DateTime lastCpuTime;
            TimeSpan lastTotalProcessorTime;

            lock (_lock)
            {
                if (_disposed) return;
                process = _targetProcess;
                cpuCounter = _cpuCounter;
                lastCpuTime = _lastCpuTime;
                lastTotalProcessorTime = _lastTotalProcessorTime;
                if (process == null) return;

                if (process.HasExited)
                {
                    Stop();
                    return;
                }
            }

            try
            {
                var memoryMb = process.WorkingSet64 / (1024.0 * 1024.0);

                double cpuUsage = 0;
                try
                {
                    if (cpuCounter != null)
                    {
                        cpuUsage = cpuCounter.NextValue();
                    }
                    else
                    {
                        lock (_lock)
                        {
                            if (_disposed || _targetProcess == null) return;
                            
                            var now = DateTime.Now;
                            var totalProcessorTime = process.TotalProcessorTime;
                            var timeDiff = (now - _lastCpuTime).TotalMilliseconds;
                            var cpuDiff = (totalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;

                            if (timeDiff > 0)
                            {
                                cpuUsage = (cpuDiff / timeDiff / Environment.ProcessorCount) * 100;
                            }

                            _lastCpuTime = now;
                            _lastTotalProcessorTime = totalProcessorTime;
                        }
                    }
                }
                catch { }

                cpuUsage = Math.Clamp(cpuUsage, 0, 100);

                var (TotalMb, WorldSizes) = GetStorageUsageCached();

                var data = new PerformanceData
                {
                    MemoryUsageMb = memoryMb,
                    CpuUsage = cpuUsage,
                    TotalStorageMb = TotalMb,
                    WorldStorageMb = WorldSizes,
                    Timestamp = DateTime.Now
                };

                DataUpdated?.Invoke(this, data);
            }
            catch (Exception)
            {
            }
        }

        private (long TotalMb, Dictionary<string, long> WorldSizes) GetStorageUsage()
        {
            var worldSizes = new Dictionary<string, long>(3);
            long totalBytes = 0;

            try
            {
                string instanceDir = PathHelper.GetInstanceDir(_instanceId);

                if (Directory.Exists(instanceDir))
                {
                    var worldPaths = new Dictionary<string, string>(3);
                    foreach (var worldName in s_worldFolders)
                    {
                        string worldPath = Path.Combine(instanceDir, worldName);
                        if (Directory.Exists(worldPath))
                        {
                            worldPaths[worldName] = worldPath;
                        }
                    }

                    totalBytes = GetDirectorySizeExcluding(instanceDir, worldPaths.Values);

                    foreach (var (worldName, worldPath) in worldPaths)
                    {
                        long sizeBytes = GetDirectorySizeFast(worldPath);
                        worldSizes[worldName] = sizeBytes / (1024 * 1024);
                    }
                }
            }
            catch { }

            return (totalBytes / (1024 * 1024), worldSizes);
        }

        private static long GetDirectorySizeExcluding(string path, IEnumerable<string> excludePaths)
        {
            var excluded = new HashSet<string>(excludePaths.Select(p => Path.GetFullPath(p)), StringComparer.OrdinalIgnoreCase);
            long size = 0;
            try
            {
                var dir = new DirectoryInfo(path);
                foreach (var fi in dir.EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }))
                {
                    try
                    {
                        if (!excluded.Any(e => fi.FullName.StartsWith(e, StringComparison.OrdinalIgnoreCase)))
                            size += fi.Length;
                    }
                    catch { }
                }
            }
            catch { }
            return size;
        }

        private static readonly string[] s_worldFolders = ["world", "world_nether", "world_the_end"];

        private (long TotalMb, Dictionary<string, long> WorldSizes) GetStorageUsageCached()
        {
            var now = DateTime.Now;
            bool needsRefresh;
            lock (_cacheLock)
            {
                needsRefresh = (now - _lastStorageCacheTime).TotalMilliseconds >= _storageCacheIntervalMs;
            }

            if (needsRefresh)
            {
                var result = GetStorageUsage();
                lock (_cacheLock)
                {
                    _cachedStorageMb = result.TotalMb;
                    _cachedWorldSizes = result.WorldSizes;
                    _lastStorageCacheTime = now;
                }
                return result;
            }

            lock (_cacheLock)
            {
                return (_cachedStorageMb, _cachedWorldSizes);
            }
        }

        private static long GetDirectorySizeFast(string path)
        {
            long size = 0;
            try
            {
                var dir = new DirectoryInfo(path);
                foreach (var fi in dir.EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }))
                {
                    try { size += fi.Length; }
                    catch { }
                }
            }
            catch { }
            return size;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                Stop();
            }
            GC.SuppressFinalize(this);
        }
    }


}
