using System;
using System.Collections.Generic;
using System.Net.Http;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    public static class ServerProviderFactory
    {
        private static HttpClient? _sharedClient;
        private static readonly Dictionary<ServerPlatform, IServerProvider> _cache = new();
        private static readonly object _lock = new();

        private static HttpClient SharedClient
        {
            get
            {
                if (_sharedClient == null)
                {
                    _sharedClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                    _sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "SimplyMinecraftServerManager/1.0");
                    _sharedClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                }
                return _sharedClient;
            }
        }

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