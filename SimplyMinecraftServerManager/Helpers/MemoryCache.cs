// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace SimplyMinecraftServerManager.Helpers
{
    /// <summary>
    /// 基于LRU（最近最少使用）策略的内存缓存实现，支持过期时间管理。
    /// </summary>
    /// <typeparam name="T">缓存值的类型。</typeparam>
    internal class MemoryCache<T>(TimeSpan defaultExpiration, int maxEntries = 1000)
    {
        private readonly ConcurrentDictionary<string, LinkedListNode<CacheEntry<T>>> _cache = new();
        private readonly LinkedList<CacheEntry<T>> _accessOrder = new();
        private readonly Lock _evictLock = new();

        /// <summary>
        /// 获取缓存值，如果键不存在或已过期则返回默认值。
        /// </summary>
        /// <param name="key">要获取的缓存键。</param>
        /// <returns>缓存的值，如果键不存在或已过期则返回 default。</returns>
        public T? Get(string key) => TryGet(key, out var value) ? value : default;

        /// <summary>
        /// 尝试获取缓存值，如果键存在且未过期则返回 true 并更新访问顺序。
        /// </summary>
        /// <param name="key">要获取的缓存键。</param>
        /// <param name="value">输出参数，包含缓存的值。</param>
        /// <returns>如果键存在且未过期则返回 true，否则返回 false。</returns>
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

        /// <summary>
        /// 设置缓存值，如果缓存已满则触发淘汰策略。
        /// </summary>
        /// <param name="key">缓存键。</param>
        /// <param name="value">要缓存的值。</param>
        /// <param name="expiration">可选的过期时间，如果为 null 则使用默认过期时间。</param>
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

        /// <summary>
        /// 移除指定键的缓存条目。
        /// </summary>
        /// <param name="key">要移除的缓存键。</param>
        public void Remove(string key)
        {
            if (_cache.TryRemove(key, out var node))
            {
                lock (_evictLock)
                    _accessOrder.Remove(node);
            }
        }

        /// <summary>
        /// 清空所有缓存条目。
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            lock (_evictLock)
                _accessOrder.Clear();
        }

        /// <summary>
        /// 获取当前缓存中的条目数量。
        /// </summary>
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

        /// <summary>
        /// 缓存条目，包含键、值和过期时间。
        /// </summary>
        private sealed class CacheEntry<TValue>
        {
            public required string Key { get; init; }
            public required TValue Value { get; init; }
            public DateTime ExpiresAt { get; init; }
        }
    }
}
