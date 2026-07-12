// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 全局服务器进程管理器，用于跟踪所有运行中的服务器进程。
    /// </summary>
    public static class ServerProcessManager
    {
        private static readonly ConcurrentDictionary<string, ServerProcess> _processes = new();
        private static readonly ConcurrentDictionary<string, DateTime> _startTimeCache = new();
        private static readonly ConcurrentDictionary<string, EventHandler<int>> _exitHandlers = new();

        /// <summary>
        /// 当实例运行状态改变时触发（实例ID, 是否运行中）。
        /// </summary>
        public static event EventHandler<(string InstanceId, bool IsRunning)>? InstanceStatusChanged;

        /// <summary>
        /// 获取指定实例的服务器进程（如果存在）。
        /// </summary>
        public static ServerProcess? GetProcess(string instanceId)
        {
            _processes.TryGetValue(instanceId, out var process);
            return process;
        }

        /// <summary>
        /// 检查指定实例是否有运行中的服务器进程。
        /// </summary>
        public static bool IsRunning(string instanceId)
        {
            if (_processes.TryGetValue(instanceId, out var process))
            {
                return process.IsRunning;
            }
            return false;
        }

        /// <summary>
        /// 获取进程启动时间（如果已知）。
        /// </summary>
        public static DateTime? GetStartTime(string instanceId)
        {
            if (_startTimeCache.TryGetValue(instanceId, out var time))
            {
                return time;
            }
            return null;
        }

        /// <summary>
        /// 注册一个服务器进程。
        /// </summary>
        public static void Register(string instanceId, ServerProcess process)
        {
            CleanupExistingProcess(instanceId);

            if (_exitHandlers.TryRemove(instanceId, out var oldHandler))
            {
                process.Exited -= oldHandler;
            }

            EventHandler<int> handler = (_, _) =>
            {
                if (_processes.TryGetValue(instanceId, out var existing) && ReferenceEquals(existing, process))
                {
                    _processes.TryRemove(instanceId, out _);
                    _startTimeCache.TryRemove(instanceId, out _);
                    _exitHandlers.TryRemove(instanceId, out _);
                    InstanceStatusChanged?.Invoke(null, (instanceId, false));
                }
            };
            _exitHandlers[instanceId] = handler;
            process.Exited += handler;

            _processes[instanceId] = process;
            _startTimeCache[instanceId] = DateTime.Now;
            InstanceStatusChanged?.Invoke(null, (instanceId, true));
        }

        private static void CleanupExistingProcess(string instanceId)
        {
            if (_processes.TryRemove(instanceId, out var oldProcess))
            {
                try
                {
                    if (oldProcess.IsRunning)
                        oldProcess.Kill();
                }
                catch { }
                try
                {
                    oldProcess.Dispose();
                }
                catch { }
            }
            _startTimeCache.TryRemove(instanceId, out _);
        }

        /// <summary>
        /// 停止并移除指定实例的服务器进程。
        /// </summary>
        public static void StopAndRemove(string instanceId)
        {
            if (_processes.TryGetValue(instanceId, out var process))
            {
                try
                {
                    if (process.IsRunning)
                        process.Stop();
                }
                catch { }

                if (process.IsRunning)
                {
                    if (!process.WaitForExit(5000))
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// 强制终止并移除指定实例的服务器进程。
        /// </summary>
        public static void KillAndRemove(string instanceId)
        {
            if (_processes.TryRemove(instanceId, out var process))
            {
                try
                {
                    if (process.IsRunning)
                        process.Kill();
                }
                catch { }
                try
                {
                    process.Dispose();
                }
                catch { }
                _startTimeCache.TryRemove(instanceId, out _);
                InstanceStatusChanged?.Invoke(null, (instanceId, false));
            }
        }

        public static IReadOnlyList<string> GetRunningInstanceIds()
        {
            var runningIds = new List<string>();
            foreach (var kvp in _processes.ToArray())
            {
                if (kvp.Value.IsRunning)
                {
                    runningIds.Add(kvp.Key);
                }
                else
                {
                    _processes.TryRemove(kvp.Key, out _);
                    _startTimeCache.TryRemove(kvp.Key, out _);
                }
            }
            return runningIds.AsReadOnly();
        }

        /// <summary>
        /// 停止所有运行中的服务器进程。
        /// </summary>
        public static void StopAll()
        {
            foreach (var kvp in _processes.ToArray())
            {
                try
                {
                    if (kvp.Value.IsRunning)
                        kvp.Value.Stop();
                }
                catch { }
            }
        }

        /// <summary>
        /// 强制终止所有运行中的服务器进程。
        /// </summary>
        public static void KillAll()
        {
            foreach (var kvp in _processes.ToArray())
            {
                try
                {
                    if (kvp.Value.IsRunning)
                        kvp.Value.Kill();
                }
                catch { }
                try
                {
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _processes.Clear();
            _startTimeCache.Clear();
        }

        /// <summary>
        /// 刷新并清理已停止的进程记录。
        /// </summary>
        public static void CleanupStoppedProcesses()
        {
            foreach (var kvp in _processes.ToArray())
            {
                if (!kvp.Value.IsRunning)
                {
                    _processes.TryRemove(kvp.Key, out _);
                    _startTimeCache.TryRemove(kvp.Key, out _);
                    InstanceStatusChanged?.Invoke(null, (kvp.Key, false));
                }
            }
        }
    }
}
