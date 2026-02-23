using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Tomlyn;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 服务器实例管理器
    /// </summary>
    public static class InstanceManager
    {
        private static readonly object _lock = new();
        private static List<InstanceInfo> _instances = new();
        private static bool _loaded;

        /// <summary>从 instances.toml 加载实例列表。</summary>
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
                }
                catch (Exception)
                {
                    _instances = new List<InstanceInfo>();
                }

                _loaded = true;
            }
        }

        /// <summary>将当前实例列表写回 instances.toml。</summary>
        private static void Save()
        {
            var model = new InstancesFileModel { Instances = _instances };
            string toml = Toml.FromModel(model);
            File.WriteAllText(PathHelper.InstancesFile, toml, Encoding.UTF8);
        }

        private static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        /// <summary>获取所有实例的只读快照。</summary>
        public static IReadOnlyList<InstanceInfo> GetAll()
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _instances.ToList().AsReadOnly();
            }
        }

        /// <summary>按 ID 获取实例，不存在返回 null。</summary>
        public static InstanceInfo? GetById(string id)
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _instances.FirstOrDefault(
                    i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>按名称模糊搜索。</summary>
        public static IReadOnlyList<InstanceInfo> Search(string keyword)
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _instances
                    .Where(i => i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                    .AsReadOnly();
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

                // 创建实例目录结构
                string instanceDir = PathHelper.GetInstanceDir(info.Id);
                Directory.CreateDirectory(instanceDir);
                Directory.CreateDirectory(PathHelper.GetPluginsDir(info.Id));

                // 复制服务端 JAR（如果提供了源路径）
                if (!string.IsNullOrWhiteSpace(serverJarSourcePath) && File.Exists(serverJarSourcePath))
                {
                    string destJar = PathHelper.GetServerJarPath(info.Id, info.ServerJar);
                    File.Copy(serverJarSourcePath, destJar, overwrite: true);
                }

                // 自动接受 EULA
                if (config.AutoAcceptEula)
                {
                    AcceptEula(info.Id);
                }

                _instances.Add(info);
                Save();

                return info;
            }
        }

        /// <summary>更新实例信息并持久化。</summary>
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
                Save();
            }
        }

        /// <summary>修改指定实例的 JDK 路径。</summary>
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
                Save();
            }
        }

        /// <summary>修改实例的内存设置。</summary>
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
                Save();
            }
        }

        /// <summary>
        /// 删除实例。
        /// </summary>
        /// <param name="instanceId">实例 UUID</param>
        /// <param name="deleteFiles">是否同时删除实例文件夹</param>
        /// <returns>是否成功删除</returns>
        public static bool DeleteInstance(string instanceId, bool deleteFiles = true)
        {
            lock (_lock)
            {
                EnsureLoaded();

                int removed = _instances.RemoveAll(
                    i => i.Id.Equals(instanceId, StringComparison.OrdinalIgnoreCase));

                if (removed == 0) return false;

                Save();

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

        /// <summary>为指定实例写入 eula=true。</summary>
        public static void AcceptEula(string instanceId)
        {
            string path = PathHelper.GetEulaPath(instanceId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "# Auto-accepted by SMSM\neula=true\n", Encoding.UTF8);
        }

        /// <summary>获取实例实际使用的 java.exe 路径（实例 > 全局默认）。</summary>
        public static string ResolveJdkPath(string instanceId)
        {
            var info = GetById(instanceId);
            if (info != null && !string.IsNullOrWhiteSpace(info.JdkPath))
                return info.JdkPath;

            return ConfigManager.Current.DefaultJdkPath;
        }

        /// <summary>强制重新从磁盘加载。</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _loaded = false;
                Load();
            }
        }
    }
}