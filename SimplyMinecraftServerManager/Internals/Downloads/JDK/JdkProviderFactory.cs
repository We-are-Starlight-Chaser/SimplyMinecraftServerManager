// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Net.Http;

namespace SimplyMinecraftServerManager.Internals.Downloads.JDK
{
    /// <summary>
    /// JDK 提供者工厂，负责创建和缓存不同发行版的 JDK 提供者实例。
    /// </summary>
    public static class JdkProviderFactory
    {
        private static HttpClient? _sharedClient;
        private static readonly Dictionary<JdkDistribution, IJdkProvider> _cache = [];
        private static readonly Lock _lock = new();

        private static HttpClient SharedClient
        {
            get
            {
                if (_sharedClient == null)
                {
                    _sharedClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                    _sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "SimplyMinecraftServerManager/1.0 (https://github.com/We-are-Starlight-Chaser/SimplyMinecraftServerManager)");
                    _sharedClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                }
                return _sharedClient;
            }
        }

        /// <summary>
        /// 获取指定发行版的 JDK 提供者实例。若已缓存则直接返回，否则创建新实例并缓存。
        /// </summary>
        /// <param name="distribution">JDK 发行版类型。</param>
        /// <returns>对应的 JDK 提供者实例。</returns>
        /// <exception cref="ArgumentOutOfRangeException">不支持的发行版类型时抛出。</exception>
        public static IJdkProvider Get(JdkDistribution distribution)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(distribution, out var cached))
                    return cached;

                IJdkProvider provider = distribution switch
                {
                    JdkDistribution.Adoptium => new AdoptiumProvider(SharedClient),
                    JdkDistribution.Zulu => new ZuluProvider(SharedClient),
                    _ => throw new ArgumentOutOfRangeException(nameof(distribution))
                };

                _cache[distribution] = provider;
                return provider;
            }
        }

        /// <summary>
        /// 获取所有已支持的 JDK 发行版提供者实例列表。
        /// </summary>
        /// <returns>所有支持的 JDK 提供者只读列表。</returns>
        public static IReadOnlyList<IJdkProvider> GetAll()
        {
            return new List<IJdkProvider>
            {
                Get(JdkDistribution.Adoptium),
                Get(JdkDistribution.Zulu),
            }.AsReadOnly();
        }
    }
}
