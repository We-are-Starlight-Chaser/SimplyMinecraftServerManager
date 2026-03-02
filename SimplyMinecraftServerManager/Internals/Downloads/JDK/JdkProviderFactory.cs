using System.Net.Http;

namespace SimplyMinecraftServerManager.Internals.Downloads.JDK
{
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
                        "SimplyMinecraftServerManager/1.0");
                    _sharedClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                }
                return _sharedClient;
            }
        }

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