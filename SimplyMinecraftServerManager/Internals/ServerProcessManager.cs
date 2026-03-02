using System.Collections.Concurrent;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 全局服务器进程管理器，用于跟踪所有运行中的服务器进程。
    /// </summary>
    public static class ServerProcessManager
    {
        private static readonly ConcurrentDictionary<string, ServerProcess> _processes = new();

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
        /// 注册一个服务器进程。
        /// </summary>
        public static void Register(string instanceId, ServerProcess process)
        {
            // 如果已有旧进程，先清理
            if (_processes.TryGetValue(instanceId, out var oldProcess))
            {
                try
                {
                    if (oldProcess.IsRunning)
                        oldProcess.Kill();
                    oldProcess.Dispose();
                }
                catch { }
            }

            _processes[instanceId] = process;

            // 当进程退出时自动从字典中移除
            process.Exited += (_, _) =>
            {
                _processes.TryRemove(instanceId, out _);
            };
        }

        /// <summary>
        /// 停止并移除指定实例的服务器进程。
        /// </summary>
        public static void StopAndRemove(string instanceId)
        {
            if (_processes.TryRemove(instanceId, out var process))
            {
                try
                {
                    if (process.IsRunning)
                        process.Stop();
                }
                catch { }
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
            }
        }

        /// <summary>
        /// 获取所有运行中的实例 ID。
        /// </summary>
        public static IReadOnlyList<string> GetRunningInstanceIds()
        {
            return _processes
                .Where(kvp => kvp.Value.IsRunning)
                .Select(kvp => kvp.Key)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// 停止所有运行中的服务器进程。
        /// </summary>
        public static void StopAll()
        {
            foreach (var kvp in _processes)
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
            foreach (var kvp in _processes)
            {
                try
                {
                    if (kvp.Value.IsRunning)
                        kvp.Value.Kill();
                }
                catch { }
            }
            _processes.Clear();
        }
    }
}
