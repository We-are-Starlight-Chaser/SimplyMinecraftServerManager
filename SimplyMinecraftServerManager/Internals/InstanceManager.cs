using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Tomlyn;

namespace SimplyMinecraftServerManager.Internals
{
    public static class InstanceManager
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new(false);
        private static readonly Lock _lock = new();
        private static List<InstanceInfo> _instances = [];
        private static bool _loaded;
        private static readonly ConcurrentDictionary<string, InstanceInfo> _idCache = new();
        private static readonly ConcurrentDictionary<string, List<InstanceInfo>> _searchCache = new();
        private static Timer? _saveDebounceTimer;
        private static bool _pendingSave;
        private static readonly Dictionary<string, string> DefaultServerProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            ["enable-query"] = "false",
            ["enable-rcon"] = "true",
            ["broadcast-rcon-to-ops"] = "false",
            ["gamemode"] = "survival",
            ["difficulty"] = "easy",
            ["max-players"] = "20",
            ["motd"] = "Simply Minecraft Server",
            ["online-mode"] = "true",
            ["pvp"] = "true",
            ["server-ip"] = "",
            ["simulation-distance"] = "10",
            ["spawn-protection"] = "16",
            ["view-distance"] = "10"
        };

        public static void Load()
        {
            lock (_lock)
            {
                PathHelper.EnsureDirectories();

                if (!File.Exists(PathHelper.InstancesFile))
                {
                    _instances = [];
                    _loaded = true;
                    Save();
                    return;
                }

                try
                {
                    string toml = File.ReadAllText(PathHelper.InstancesFile, Encoding.UTF8);
                    var model = Toml.ToModel<InstancesFileModel>(toml);
                    _instances = model.Instances ?? [];
                    RebuildCache();
                }
                catch (Exception)
                {
                    _instances = [];
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

        private static int GetNextAvailablePort()
        {
            var usedPorts = new HashSet<int>();

            foreach (var instance in _instances)
            {
                try
                {
                    var port = ServerPropertiesManager.GetInt(instance.Id, "server-port", 25565);
                    if (port > 0)
                    {
                        usedPorts.Add(port);
                    }
                }
                catch
                {
                }
            }

            const int basePort = 25565;
            for (var port = basePort; port <= 65535; port++)
            {
                if (!usedPorts.Contains(port))
                {
                    return port;
                }
            }

            return basePort;
        }

        private static void InitializeInstanceFiles(InstanceInfo info, AppConfig config)
        {
            var instanceDir = PathHelper.GetInstanceDir(info.Id);
            Directory.CreateDirectory(instanceDir);
            Directory.CreateDirectory(PathHelper.GetPluginsDir(info.Id));

            var properties = new Dictionary<string, string>(DefaultServerProperties, StringComparer.OrdinalIgnoreCase)
            {
                ["motd"] = info.Name,
                ["server-port"] = GetNextAvailablePort().ToString()
            };

            var propertiesPath = PathHelper.GetServerPropertiesPath(info.Id);
            if (!File.Exists(propertiesPath))
            {
                ServerPropertiesManager.WriteAll(info.Id, properties);
            }

            EnsureRconConfiguration(info.Id);

            if (config.AutoAcceptEula)
            {
                AcceptEula(info.Id);
            }
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
                    [.. _instances.Where(i => i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))]
                ).AsReadOnly();
            }
        }

        /// <summary>
        /// 创建新实例。
        /// </summary>
        /// <param name="name">实例名称</param>
        /// <param name="serverType">保留参数，用于创建流程中的下载/显示逻辑，不持久化</param>
        /// <param name="minecraftVersion">保留参数，用于创建流程中的 JDK 选择等逻辑，不持久化</param>
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

                if (!Path.IsPathRooted(serverJarSourcePath))
                    throw new ArgumentException("Invalid path in server JAR source", nameof(serverJarSourcePath));

                serverJarSourcePath = Path.GetFullPath(serverJarSourcePath);
            }

            if (!SecurityHelper.IsValidFileName(serverJar))
                throw new ArgumentException("Invalid server JAR filename", nameof(serverJar));

            if (!SecurityHelper.IsValidJvmArgs(extraJvmArgs))
                throw new ArgumentException("JVM arguments contain dangerous patterns", nameof(extraJvmArgs));

            if (!string.IsNullOrWhiteSpace(jdkPath) && !Path.IsPathRooted(jdkPath))
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
                    MinMemoryMb = minMemoryMb > 0 ? minMemoryMb : config.DefaultMinMemoryMb,
                    MaxMemoryMb = maxMemoryMb > 0 ? maxMemoryMb : config.DefaultMaxMemoryMb,
                    ExtraJvmArgs = extraJvmArgs,
                    CreatedAt = DateTime.Now.ToString("O")
                };

                if (info.MinMemoryMb <= 0)
                {
                    info.MinMemoryMb = config.DefaultMinMemoryMb;
                }

                if (info.MaxMemoryMb < info.MinMemoryMb)
                {
                    info.MaxMemoryMb = Math.Max(info.MinMemoryMb, config.DefaultMaxMemoryMb);
                }

                InitializeInstanceFiles(info, config);

                if (!string.IsNullOrWhiteSpace(serverJarSourcePath) && File.Exists(serverJarSourcePath))
                {
                    string destJar = PathHelper.GetServerJarPath(info.Id, info.ServerJar);
                    File.Copy(serverJarSourcePath, destJar, overwrite: true);
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
                    i => i.Id.Equals(instanceId, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException($"Instance '{instanceId}' not found.");
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
                    i => i.Id.Equals(instanceId, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException($"Instance '{instanceId}' not found.");
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
            File.WriteAllText(path, "# Auto-accepted by SMSM\neula=true\n", Utf8WithoutBom);
        }

        public static RconConnectionInfo EnsureRconConfiguration(string instanceId)
        {
            EnsureLoaded();

            var props = ServerPropertiesManager.Read(instanceId);
            int serverPort = GetPositivePort(props.GetValueOrDefault("server-port"), 25565);
            int preferredRconPort = serverPort switch
            {
                <= 55535 => serverPort + 10000,
                _ => 25575
            };

            int rconPort = GetPositivePort(props.GetValueOrDefault("rcon.port"), 0);
            if (rconPort <= 0 || rconPort == serverPort)
            {
                rconPort = GetNextAvailableRconPort(instanceId, preferredRconPort);
            }

            string password = props.GetValueOrDefault("rcon.password", string.Empty);
            if (string.IsNullOrWhiteSpace(password) || password.Length < 16)
            {
                password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            }

            string host = props.GetValueOrDefault("server-ip", string.Empty);
            if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" || host == "::")
            {
                host = "127.0.0.1";
            }

            ServerPropertiesManager.SetValues(instanceId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["enable-rcon"] = "true",
                ["broadcast-rcon-to-ops"] = "false",
                ["rcon.port"] = rconPort.ToString(),
                ["rcon.password"] = password
            });

            return new RconConnectionInfo
            {
                Host = host,
                Port = rconPort,
                Password = password
            };
        }

        public static RconConnectionInfo GetRconConnectionInfo(string instanceId)
        {
            var props = ServerPropertiesManager.Read(instanceId);
            string host = props.GetValueOrDefault("server-ip", string.Empty);
            if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" || host == "::")
            {
                host = "127.0.0.1";
            }

            int port = GetPositivePort(props.GetValueOrDefault("rcon.port"), 0);
            string password = props.GetValueOrDefault("rcon.password", string.Empty);

            if (port <= 0 || string.IsNullOrWhiteSpace(password))
            {
                return EnsureRconConfiguration(instanceId);
            }

            return new RconConnectionInfo
            {
                Host = host,
                Port = port,
                Password = password
            };
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
            lock (_lock)
            {
                if (_pendingSave)
                {
                    Save();
                    _pendingSave = false;
                }
            }

            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = null;
        }

        private static int GetNextAvailableRconPort(string instanceId, int preferredPort)
        {
            var usedPorts = new HashSet<int>();

            foreach (var instance in _instances)
            {
                if (string.Equals(instance.Id, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    int port = ServerPropertiesManager.GetInt(instance.Id, "rcon.port", 0);
                    if (port > 0)
                    {
                        usedPorts.Add(port);
                    }
                }
                catch
                {
                }
            }

            if (preferredPort > 0 && preferredPort <= 65535 && !usedPorts.Contains(preferredPort))
            {
                return preferredPort;
            }

            for (int port = 25575; port <= 65535; port++)
            {
                if (!usedPorts.Contains(port))
                {
                    return port;
                }
            }

            return 25575;
        }

        private static int GetPositivePort(string? rawValue, int fallbackValue)
        {
            return int.TryParse(rawValue, out int port) && port is > 0 and <= 65535
                ? port
                : fallbackValue;
        }
    }
}
