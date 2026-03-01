using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Tomlyn;

namespace SimplyMinecraftServerManager.Internals
{
    public static class InstanceManager
    {
        private static readonly object _lock = new();
        private static List<InstanceInfo> _instances = new();
        private static bool _loaded;
        private static readonly ConcurrentDictionary<string, InstanceInfo> _idCache = new();
        private static readonly ConcurrentDictionary<string, List<InstanceInfo>> _searchCache = new();
        private static Timer? _saveDebounceTimer;
        private static bool _pendingSave;

        public static void Load()
        {
            lock (_lock)
            {
                PathHelper.EnsureDirectories();

                if (!File.Exists(PathHelper.InstancesFile))
                {
                    _instances = new List<InstanceInfo>();
                    _loaded = true;
                    Save();
                    return;
                }

                try
                {
                    string toml = File.ReadAllText(PathHelper.InstancesFile, Encoding.UTF8);
                    var model = Toml.ToModel<InstancesFileModel>(toml);
                    _instances = model.Instances ?? new List<InstanceInfo>();
                    RebuildCache();
                }
                catch (Exception)
                {
                    _instances = new List<InstanceInfo>();
                }

                _loaded = true;
            }
        }

        private static void RebuildCache()
        {
            _idCache.Clear();
            _searchCache.Clear();
            foreach (var instance in _instances)
            {
                _idCache[instance.Id] = instance;
            }
        }

        private static void Save()
        {
            var model = new InstancesFileModel { Instances = _instances };
            string toml = Toml.FromModel(model);
            File.WriteAllText(PathHelper.InstancesFile, toml, Encoding.UTF8);
        }

        private static void DebouncedSave()
        {
            if (_pendingSave) return;
            _pendingSave = true;
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = new Timer(_ =>
            {
                lock (_lock)
                {
                    if (_pendingSave)
                    {
                        Save();
                        _pendingSave = false;
                    }
                }
            }, null, 500, Timeout.Infinite);
        }

        private static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        public static IReadOnlyList<InstanceInfo> GetAll()
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _instances.ToList().AsReadOnly();
            }
        }

        public static InstanceInfo? GetById(string id)
        {
            EnsureLoaded();
            return _idCache.GetValueOrDefault(id);
        }

        public static IReadOnlyList<InstanceInfo> Search(string keyword)
        {
            lock (_lock)
            {
                EnsureLoaded();
                string lower = keyword.ToLowerInvariant();
                return _searchCache.GetOrAdd(lower, _ =>
                    _instances
                        .Where(i => i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        .ToList()
                ).AsReadOnly();
            }
        }

        /// <summary>
        /// 创建新实例。
        /// </summary>
        /// <param name="name">实例名称</param>
        /// <param name="serverType">服务端类型 (paper/spigot/vanilla…)</param>
        /// <param name="minecraftVersion">MC 版本</param>
        /// <param name="jdkPath">JDK 路径，留空使用全局默认</param>
        /// <param name="serverJar">服务端 JAR 文件名</param>
        /// <param name="serverJarSourcePath">
        /// 可选：源 JAR 文件的完整路径，提供后会复制到实例目录。
        /// </param>
        /// <param name="minMemoryMb">最小内存 MB</param>
        /// <param name="maxMemoryMb">最大内存 MB</param>
        /// <param name="extraJvmArgs">额外 JVM 参数</param>
        /// <returns>创建好的 InstanceInfo</returns>
        public static InstanceInfo CreateInstance(
            string name,
            string serverType = "vanilla",
            string minecraftVersion = "",
            string jdkPath = "",
            string serverJar = "server.jar",
            string? serverJarSourcePath = null,
            int minMemoryMb = 0,
            int maxMemoryMb = 0,
            string extraJvmArgs = "")
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Instance name cannot be empty", nameof(name));

            name = SecurityHelper.SanitizeInstanceName(name);

            if (!SecurityHelper.IsValidInstanceName(name))
                throw new ArgumentException("Invalid instance name format", nameof(name));

            if (!string.IsNullOrWhiteSpace(serverJarSourcePath))
            {
                if (!File.Exists(serverJarSourcePath))
                    throw new FileNotFoundException("Server JAR source not found", serverJarSourcePath);

                if (SecurityHelper.IsPathTraversal(serverJarSourcePath))
                    throw new ArgumentException("Invalid path in server JAR source", nameof(serverJarSourcePath));

                serverJarSourcePath = Path.GetFullPath(serverJarSourcePath);
            }

            if (!SecurityHelper.IsValidFileName(serverJar))
                throw new ArgumentException("Invalid server JAR filename", nameof(serverJar));

            if (!SecurityHelper.IsValidJvmArgs(extraJvmArgs))
                throw new ArgumentException("JVM arguments contain dangerous patterns", nameof(extraJvmArgs));

            if (!string.IsNullOrWhiteSpace(jdkPath) && SecurityHelper.IsPathTraversal(jdkPath))
                throw new ArgumentException("Invalid JDK path", nameof(jdkPath));

            lock (_lock)
            {
                EnsureLoaded();

                var config = ConfigManager.Current;

                var info = new InstanceInfo
                {
                    Id = Guid.NewGuid().ToString("D"),
                    Name = name,
                    JdkPath = jdkPath,
                    ServerJar = serverJar,
                    ServerType = serverType,
                    MinecraftVersion = minecraftVersion,
                    MinMemoryMb = minMemoryMb > 0 ? minMemoryMb : config.DefaultMinMemoryMb,
                    MaxMemoryMb = maxMemoryMb > 0 ? maxMemoryMb : config.DefaultMaxMemoryMb,
                    ExtraJvmArgs = extraJvmArgs,
                    CreatedAt = DateTime.Now.ToString("O")
                };

                string instanceDir = PathHelper.GetInstanceDir(info.Id);
                if (SecurityHelper.IsPathTraversal(instanceDir))
                    throw new InvalidOperationException("Invalid instance directory path");

                Directory.CreateDirectory(instanceDir);
                Directory.CreateDirectory(PathHelper.GetPluginsDir(info.Id));

                if (!string.IsNullOrWhiteSpace(serverJarSourcePath) && File.Exists(serverJarSourcePath))
                {
                    string destJar = PathHelper.GetServerJarPath(info.Id, info.ServerJar);
                    File.Copy(serverJarSourcePath, destJar, overwrite: true);
                }

                if (config.AutoAcceptEula)
                {
                    AcceptEula(info.Id);
                }

                _instances.Add(info);
                _idCache[info.Id] = info;
                _searchCache.Clear();
                DebouncedSave();

                return info;
            }
        }

        public static void UpdateInstance(InstanceInfo info)
        {
            lock (_lock)
            {
                EnsureLoaded();

                int idx = _instances.FindIndex(
                    i => i.Id.Equals(info.Id, StringComparison.OrdinalIgnoreCase));

                if (idx < 0)
                    throw new KeyNotFoundException($"Instance '{info.Id}' not found.");

                _instances[idx] = info;
                _idCache[info.Id] = info;
                _searchCache.Clear();
                DebouncedSave();
            }
        }

        public static void SetJdkPath(string instanceId, string jdkPath)
        {
            lock (_lock)
            {
                EnsureLoaded();

                var info = _instances.FirstOrDefault(
                    i => i.Id.Equals(instanceId, StringComparison.OrdinalIgnoreCase));

                if (info == null)
                    throw new KeyNotFoundException($"Instance '{instanceId}' not found.");

                info.JdkPath = jdkPath;
                _idCache[instanceId] = info;
                DebouncedSave();
            }
        }

        public static void SetMemory(string instanceId, int minMb, int maxMb)
        {
            lock (_lock)
            {
                EnsureLoaded();

                var info = _instances.FirstOrDefault(
                    i => i.Id.Equals(instanceId, StringComparison.OrdinalIgnoreCase));

                if (info == null)
                    throw new KeyNotFoundException($"Instance '{instanceId}' not found.");

                info.MinMemoryMb = minMb;
                info.MaxMemoryMb = maxMb;
                _idCache[instanceId] = info;
                DebouncedSave();
            }
        }

        public static bool DeleteInstance(string instanceId, bool deleteFiles = true)
        {
            lock (_lock)
            {
                EnsureLoaded();

                int removed = _instances.RemoveAll(
                    i => i.Id.Equals(instanceId, StringComparison.OrdinalIgnoreCase));

                if (removed == 0) return false;

                _idCache.TryRemove(instanceId, out _);
                _searchCache.Clear();
                DebouncedSave();

                if (deleteFiles)
                {
                    string dir = PathHelper.GetInstanceDir(instanceId);
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }

                return true;
            }
        }

        public static void AcceptEula(string instanceId)
        {
            string path = PathHelper.GetEulaPath(instanceId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "# Auto-accepted by SMSM\neula=true\n", Encoding.UTF8);
        }

        public static string ResolveJdkPath(string instanceId)
        {
            var info = GetById(instanceId);
            if (info != null && !string.IsNullOrWhiteSpace(info.JdkPath))
                return info.JdkPath;

            return ConfigManager.Current.DefaultJdkPath;
        }

        public static void Reload()
        {
            lock (_lock)
            {
                _loaded = false;
                _idCache.Clear();
                _searchCache.Clear();
                Load();
            }
        }

        public static void Shutdown()
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = null;
        }
    }
}