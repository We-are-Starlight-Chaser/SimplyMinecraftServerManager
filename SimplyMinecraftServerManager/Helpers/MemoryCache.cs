using System.Collections.Concurrent;

namespace SimplyMinecraftServerManager.Helpers
{
    internal class MemoryCache<T>(TimeSpan defaultExpiration, int maxEntries = 1000)
    {
        private readonly ConcurrentDictionary<string, LinkedListNode<CacheEntry<T>>> _cache = new();
        private readonly LinkedList<CacheEntry<T>> _accessOrder = new();
        private readonly Lock _evictLock = new();

        public T? Get(string key) => TryGet(key, out var value) ? value : default;

        public bool TryGet(string key, out T? value)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                if (node.Value.ExpiresAt > DateTime.UtcNow)
                {
                    Touch(node);
                    value = node.Value.Value;
                    return true;
                }
                RemoveFromList(key, node);
            }
            value = default;
            return false;
        }

        public void Set(string key, T value, TimeSpan? expiration = null)
        {
            if (_cache.Count >= maxEntries)
                EvictOne();

            var entry = new CacheEntry<T>
            {
                Key = key,
                Value = value,
                ExpiresAt = DateTime.UtcNow + (expiration ?? defaultExpiration),
            };

            lock (_evictLock)
            {
                var node = _accessOrder.AddLast(entry);
                if (_cache.TryAdd(key, node))
                    return;
                _accessOrder.RemoveLast();
            }
        }

        public void Remove(string key)
        {
            if (_cache.TryRemove(key, out var node))
            {
                lock (_evictLock)
                    _accessOrder.Remove(node);
            }
        }

        public void Clear()
        {
            _cache.Clear();
            lock (_evictLock)
                _accessOrder.Clear();
        }

        public int Count => _cache.Count;

        private void Touch(LinkedListNode<CacheEntry<T>> node)
        {
            if (node.Next == null)
                return;
            lock (_evictLock)
            {
                if (node.List == null)
                    return;
                _accessOrder.Remove(node);
                _accessOrder.AddLast(node);
            }
        }

        private void RemoveFromList(string key, LinkedListNode<CacheEntry<T>> node)
        {
            _cache.TryRemove(key, out _);
            lock (_evictLock)
            {
                if (node.List != null)
                    _accessOrder.Remove(node);
            }
        }

        private void EvictOne()
        {
            lock (_evictLock)
            {
                var node = _accessOrder.First;
                while (node != null)
                {
                    var next = node.Next;
                    if (node.Value.ExpiresAt <= DateTime.UtcNow)
                    {
                        _cache.TryRemove(node.Value.Key, out _);
                        _accessOrder.Remove(node);
                        return;
                    }
                    node = next;
                }

                node = _accessOrder.First;
                if (node != null)
                {
                    _cache.TryRemove(node.Value.Key, out _);
                    _accessOrder.Remove(node);
                }
            }
        }

        private sealed class CacheEntry<TValue>
        {
            public required string Key { get; init; }
            public required TValue Value { get; init; }
            public DateTime ExpiresAt { get; init; }
        }
    }
}
