// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Tomlyn;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 服务器实例管理器，负责实例的增删改查、持久化和端口分配。
    /// </summary>
    public static class InstanceManager
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new(false);
        private static readonly Lock _lock = new();
        private static List<InstanceInfo> _instances = [];
        private static bool _loaded;
        private static readonly ConcurrentDictionary<string, InstanceInfo> _idCache = new();
        private static readonly ConcurrentDictionary<string, List<InstanceInfo>> _searchCache = new();
        private static readonly HashSet<int> _usedServerPorts = [];
        private static readonly HashSet<int> _usedRconPorts = [];
        private static readonly Timer _saveDebounceTimer = new(_ => FlushSave());
        private static volatile bool _pendingSave;
        private static readonly Lock _saveLock = new();

        private static void FlushSave()
        {
            if (!_pendingSave) return;
            lock (_saveLock)
            {
                if (!_pendingSave) return;
                Save();
                _pendingSave = false;
            }
        }
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

        /// <summary>从实例列表文件加载所有实例数据</summary>
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
            _usedServerPorts.Clear();
            _usedRconPorts.Clear();
            foreach (var instance in _instances)
            {
                _idCache[instance.Id] = instance;
            }
            RebuildPortCaches();
        }

        private static void RebuildPortCaches()
        {
            _usedServerPorts.Clear();
            _usedRconPorts.Clear();
            foreach (var instance in _instances)
            {
                try
                {
                    var port = ServerPropertiesManager.GetInt(instance.Id, "server-port", 0);
                    if (port > 0) _usedServerPorts.Add(port);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[InstanceManager] Failed to read server-port for {instance.Id}: {ex.Message}");
                }

                try
                {
                    var rconPort = ServerPropertiesManager.GetInt(instance.Id, "rcon.port", 0);
                    if (rconPort > 0) _usedRconPorts.Add(rconPort);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[InstanceManager] Failed to read rcon.port for {instance.Id}: {ex.Message}");
                }
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
            lock (_saveLock)
            {
                if (_pendingSave) return;
                _pendingSave = true;
                _saveDebounceTimer.Change(500, Timeout.Infinite);
            }
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (!_loaded) Load();
            }
        }

        private static int GetNextAvailablePort()
        {
            const int basePort = 25565;
            for (var port = basePort; port <= 65535; port++)
            {
                if (!_usedServerPorts.Contains(port))
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

        /// <summary>获取所有实例的只读列表</summary>
        /// <returns>实例信息列表</returns>
        public static IReadOnlyList<InstanceInfo> GetAll()
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _instances.AsReadOnly();
            }
        }

        /// <summary>根据 ID 查找实例</summary>
        /// <param name="id">实例唯一标识</param>
        /// <returns>找到的实例信息，未找到则返回 null</returns>
        public static InstanceInfo? GetById(string id)
        {
            EnsureLoaded();
            return _idCache.TryGetValue(id, out var info) ? info : null;
        }

        /// <summary>按名称关键字搜索实例</summary>
        /// <param name="keyword">搜索关键字</param>
        /// <returns>匹配的实例列表</returns>
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
                try
                {
                    var port = ServerPropertiesManager.GetInt(info.Id, "server-port", 0);
                    if (port > 0) _usedServerPorts.Add(port);
                    var rconPort = ServerPropertiesManager.GetInt(info.Id, "rcon.port", 0);
                    if (rconPort > 0) _usedRconPorts.Add(rconPort);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[InstanceManager] Failed to read ports for new instance {info.Id}: {ex.Message}");
                }
                DebouncedSave();

                return info;
            }
        }

        /// <summary>
        /// 更新已有实例的信息。
        /// </summary>
        /// <param name="info">更新后的实例信息</param>
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

        /// <summary>
        /// 设置指定实例的 JDK 路径。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="jdkPath">JDK 完整路径</param>
public static void SetJdkPath(string instanceId, string jdkPath)
        {
            if (string.IsNullOrWhiteSpace(jdkPath))
                throw new ArgumentException("JDK path cannot be empty.", nameof(jdkPath));

            if (!Path.IsPathRooted(jdkPath))
                throw new ArgumentException("JDK path must be an absolute path.", nameof(jdkPath));

            jdkPath = Path.GetFullPath(jdkPath);

            if (!File.Exists(jdkPath))
                throw new FileNotFoundException("JDK executable not found.", jdkPath);

            lock (_lock)
            {
                EnsureLoaded();
                
                if (!_idCache.TryGetValue(instanceId, out var info))
                    throw new KeyNotFoundException($"Instance '{instanceId}' not found.");
                
                info.JdkPath = jdkPath;
                _idCache[instanceId] = info;
                DebouncedSave();
            }
        }

        /// <summary>
        /// 设置指定实例的内存分配。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="minMb">最小内存 (MB)，必须为正数且为 4 的倍数</param>
        /// <param name="maxMb">最大内存 (MB)，必须为正数、不小于 minMb 且为 4 的倍数</param>
        public static void SetMemory(string instanceId, int minMb, int maxMb)
        {
            if (minMb <= 0)
                throw new ArgumentOutOfRangeException(nameof(minMb), minMb, "Minimum memory must be positive.");
            if (maxMb <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxMb), maxMb, "Maximum memory must be positive.");
            if (minMb % 4 != 0)
                throw new ArgumentOutOfRangeException(nameof(minMb), minMb, "Minimum memory must be a multiple of 4.");
            if (maxMb % 4 != 0)
                throw new ArgumentOutOfRangeException(nameof(maxMb), maxMb, "Maximum memory must be a multiple of 4.");
            if (maxMb < minMb)
                throw new ArgumentOutOfRangeException(nameof(maxMb), maxMb, "Maximum memory must not be less than minimum memory.");

            lock (_lock)
            {
                EnsureLoaded();

                if (!_idCache.TryGetValue(instanceId, out var info))
                    throw new KeyNotFoundException($"Instance '{instanceId}' not found.");
                
                info.MinMemoryMb = minMb;
                info.MaxMemoryMb = maxMb;
                _idCache[instanceId] = info;
                DebouncedSave();
            }
        }

        /// <summary>
        /// 删除指定实例，可选是否同时删除磁盘文件。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <param name="deleteFiles">是否删除实例目录及文件</param>
        /// <returns>是否成功删除</returns>
        public static bool DeleteInstance(string instanceId, bool deleteFiles = true)
        {
            string? dirToDelete = null;
            
            lock (_lock)
            {
                EnsureLoaded();

                int removed = _instances.RemoveAll(
                    i => i.Id.Equals(instanceId, StringComparison.OrdinalIgnoreCase));

                if (removed == 0) return false;

                _idCache.TryRemove(instanceId, out _);
                _searchCache.Clear();
                RebuildPortCaches();
                DebouncedSave();

                if (deleteFiles)
                {
                    string dir = PathHelper.GetInstanceDir(instanceId);
                    if (Directory.Exists(dir))
                    {
                        dirToDelete = dir;
                    }
                }
            }

            if (dirToDelete != null)
            {
                try
                {
                    Directory.Delete(dirToDelete, recursive: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[InstanceManager] Failed to delete instance directory {dirToDelete}: {ex.Message}");
                }
            }

            return true;
        }

        /// <summary>
        /// 自动接受指定实例的 Minecraft EULA 协议。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        public static void AcceptEula(string instanceId)
        {
            string path = PathHelper.GetEulaPath(instanceId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "# Auto-accepted by SMSM\neula=true\n", Utf8WithoutBom);
        }

        /// <summary>
        /// 确保指定实例的 RCON 配置完整，若缺失则自动生成。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <returns>RCON 连接信息</returns>
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

        /// <summary>
        /// 获取指定实例的 RCON 连接信息，若未配置则自动初始化。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <returns>RCON 连接信息</returns>
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

        /// <summary>
        /// 解析实例的 JDK 路径：优先使用实例自定义路径，否则使用全局默认。
        /// </summary>
        /// <param name="instanceId">实例 ID</param>
        /// <returns>有效的 JDK 可执行文件路径</returns>
        public static string ResolveJdkPath(string instanceId)
        {
            var info = GetById(instanceId);
            if (info != null && !string.IsNullOrWhiteSpace(info.JdkPath))
                return info.JdkPath;

            return ConfigManager.Current.DefaultJdkPath;
        }

        /// <summary>重新加载实例列表（清除缓存后重新从文件读取）</summary>
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

        /// <summary>关闭实例管理器，刷新待保存数据并释放资源</summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_pendingSave)
                {
                    FlushSave();
                }
            }

            _saveDebounceTimer.Dispose();
        }

        private static int GetNextAvailableRconPort(string instanceId, int preferredPort)
        {
            if (preferredPort > 0 && preferredPort <= 65535 && !_usedRconPorts.Contains(preferredPort))
            {
                return preferredPort;
            }

            for (int port = 25575; port <= 65535; port++)
            {
                if (!_usedRconPorts.Contains(port))
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
