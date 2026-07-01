// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Net.Http;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 服务端提供者工厂，负责创建和缓存各平台的下载提供者实例。
    /// 支持 Paper、Folia、Velocity、Purpur、Leaves、Leaf 等平台。
    /// </summary>
    public static class ServerProviderFactory
    {
        private static HttpClient? _sharedClient;
        private static readonly Dictionary<ServerPlatform, IServerProvider> _cache = [];
        private static readonly Lock _lock = new();

        private static HttpClient SharedClient
        {
            get
            {
                if (_sharedClient == null)
                {
                    var handler = new SocketsHttpHandler
                    {
                        AutomaticDecompression = System.Net.DecompressionMethods.All,
                    };
                    _sharedClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
                    _sharedClient.DefaultRequestHeaders.TryAddWithoutValidation(
                        "User-Agent", "SimplyMinecraftServerManager/1.0 (https://github.com/We-are-Starlight-Chaser/SimplyMinecraftServerManager)");
                    _sharedClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
                }
                return _sharedClient;
            }
        }

        /// <summary>
        /// 获取指定平台的服务端提供者实例（带缓存）。
        /// </summary>
        /// <param name="platform">服务端平台枚举</param>
        /// <returns>对应平台的服务端提供者</returns>
        public static IServerProvider Get(ServerPlatform platform)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(platform, out var cached))
                    return cached;

                IServerProvider provider = platform switch
                {
                    ServerPlatform.Paper => new PaperProvider(ServerPlatform.Paper, "paper", SharedClient),
                    ServerPlatform.Folia => new PaperProvider(ServerPlatform.Folia, "folia", SharedClient),
                    ServerPlatform.Velocity => new PaperProvider(ServerPlatform.Velocity, "velocity", SharedClient),
                    ServerPlatform.Purpur => new PurpurProvider(SharedClient),
                    ServerPlatform.Leaves => new LeavesProvider(SharedClient),
                    ServerPlatform.Leaf => new LeafProvider(SharedClient),
                    _ => throw new ArgumentOutOfRangeException(nameof(platform))
                };

                _cache[platform] = provider;
                return provider;
            }
        }

        /// <summary>
        /// 获取所有已注册平台的服务端提供者（含代理端）。
        /// </summary>
        /// <returns>所有平台提供者的只读列表</returns>
        public static IReadOnlyList<IServerProvider> GetAll()
        {
            var list = new List<IServerProvider>();
            foreach (ServerPlatform p in Enum.GetValues<ServerPlatform>())
                list.Add(Get(p));
            return list.AsReadOnly();
        }

        /// <summary>
        /// 获取所有游戏服务端提供者（不含 Velocity 代理端）。
        /// </summary>
        public static IReadOnlyList<IServerProvider> GetGameServers()
        {
            return new List<IServerProvider>
            {
                Get(ServerPlatform.Paper),
                Get(ServerPlatform.Folia),
                Get(ServerPlatform.Purpur),
                Get(ServerPlatform.Leaves),
                Get(ServerPlatform.Leaf),
            }.AsReadOnly();
        }

        /// <summary>
        /// 获取所有代理端提供者。
        /// </summary>
        public static IReadOnlyList<IServerProvider> GetProxies()
        {
            return new List<IServerProvider>
            {
                Get(ServerPlatform.Velocity),
            }.AsReadOnly();
        }
    }
}