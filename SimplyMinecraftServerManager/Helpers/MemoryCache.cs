using System.Collections.Concurrent;

namespace SimplyMinecraftServerManager.Helpers
{
    internal class MemoryCache<T>(TimeSpan defaultExpiration, int maxEntries = 1000)
    {
        private readonly ConcurrentDictionary<string, CacheEntry<T>> _cache = new();
        private readonly TimeSpan _defaultExpiration = defaultExpiration;
        private readonly int _maxEntries = maxEntries;

        public T? Get(string key) => TryGet(key, out var value) ? value : default;

        public bool TryGet(string key, out T? value)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt > DateTime.UtcNow)
                {
                    entry.LastAccessed = DateTime.UtcNow;
                    value = entry.Value;
                    return true;
                }
                _cache.TryRemove(key, out _);
            }
            value = default;
            return false;
        }

        public void Set(string key, T value, TimeSpan? expiration = null)
        {
            if (_cache.Count >= _maxEntries)
            {
                EvictOldest();
            }

            _cache[key] = new CacheEntry<T>
            {
                Value = value,
                ExpiresAt = DateTime.UtcNow + (expiration ?? _defaultExpiration),
                CreatedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow
            };
        }

        public void Remove(string key) => _cache.TryRemove(key, out _);

        public void Clear() => _cache.Clear();

        public int Count => _cache.Count;

        private void EvictOldest()
        {
            var oldest = _cache.OrderBy(x => x.Value.LastAccessed).Take(_maxEntries / 10).Select(x => x.Key).ToList();
            foreach (var key in oldest)
            {
                _cache.TryRemove(key, out _);
            }
        }

        private class CacheEntry<TValue>
        {
            public required TValue Value { get; init; }
            public DateTime ExpiresAt { get; init; }
            public DateTime CreatedAt { get; init; }
            public DateTime LastAccessed { get; set; }
        }
    }

    internal static class CacheExtensions
    {
        private static readonly MemoryCache<string> _stringCache = new(TimeSpan.FromMinutes(30), 500);
        private static readonly MemoryCache<byte[]> _bytesCache = new(TimeSpan.FromMinutes(10), 100);
        private static readonly MemoryCache<object> _objectCache = new(TimeSpan.FromMinutes(5), 200);

        public static string? GetCachedString(string key) => _stringCache.Get(key);

        public static void CacheString(string key, string value, TimeSpan? expiration = null)
            => _stringCache.Set(key, value, expiration);

        public static byte[]? GetCachedBytes(string key) => _bytesCache.Get(key);

        public static void CacheBytes(string key, byte[] value, TimeSpan? expiration = null)
            => _bytesCache.Set(key, value, expiration);

        public static T? GetCachedObject<T>(string key) where T : class
            => _objectCache.Get(key) as T;

        public static void CacheObject<T>(string key, T value, TimeSpan? expiration = null) where T : class
            => _objectCache.Set(key, value, expiration);

        public static void ClearAllCaches()
        {
            _stringCache.Clear();
            _bytesCache.Clear();
            _objectCache.Clear();
        }
    }
}