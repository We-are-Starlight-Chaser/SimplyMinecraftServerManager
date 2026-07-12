// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SimplyMinecraftServerManager.Helpers
{
    /// <summary>
    /// 基于LRU（最近最少使用）策略的内存缓存实现，支持过期时间管理。
    /// </summary>
    /// <typeparam name="T">缓存值的类型。</typeparam>
    internal class MemoryCache<T>(TimeSpan defaultExpiration, int maxEntries = 1000)
    {
        private readonly ConcurrentDictionary<string, LinkedListNode<CacheEntry>> _cache = new();
        private readonly LinkedList<CacheEntry> _accessOrder = new();
        private readonly Lock _evictLock = new();
        private readonly TimeSpan _defaultTtl = defaultExpiration;
        private readonly int _maxCapacity = maxEntries;

        /// <summary>
        /// 当前缓存有效条目总数
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// 获取缓存值，不存在/过期返回 default(T)
        /// </summary>
        public T? Get(string key) => TryGet(key, out var val) ? val : default;

        /// <summary>
        /// 尝试获取缓存，命中则自动刷新访问时序与过期续期
        /// </summary>
        public bool TryGet(string key, out T? value)
        {
            value = default;
            if (!_cache.TryGetValue(key, out var node))
                return false;

            var entry = node.Value;
            if (entry.ExpiresAt <= DateTime.UtcNow)
            {
                RemoveInternal(key, node);
                return false;
            }

            Touch(node);
            value = entry.Value;
            return true;
        }

        /// <summary>
        /// 写入/更新缓存，支持自定义过期时长
        /// </summary>
        public void Set(string key, T value, TimeSpan? expiration = null)
        {
            var ttl = expiration ?? _defaultTtl;
            if (ttl <= TimeSpan.Zero)
                return;

            var newEntry = new CacheEntry
            {
                Key = key,
                Value = value,
                ExpiresAt = DateTime.UtcNow + ttl
            };

            lock (_evictLock)
            {
                // 存在旧条目先彻底移除
                if (_cache.TryGetValue(key, out var oldNode))
                {
                    _accessOrder.Remove(oldNode);
                    _cache.TryRemove(key, out _);
                }

                // 容量超限循环淘汰
                while (_cache.Count >= _maxCapacity)
                    EvictOne();

                var newNode = _accessOrder.AddLast(newEntry);
                _cache.TryAdd(key, newNode);
            }
        }

        /// <summary>
        /// 不存在则通过工厂创建并存入缓存，存在直接返回已有值
        /// </summary>
        public T GetOrCreate(string key, Func<T> factory, TimeSpan? expiration = null)
        {
            if (TryGet(key, out var existValue))
                return existValue!;

            var newValue = factory();
            Set(key, newValue, expiration);
            return newValue;
        }

        /// <summary>
        /// 手动删除指定Key缓存
        /// </summary>
        public void Remove(string key)
        {
            if (_cache.TryRemove(key, out var node))
            {
                lock (_evictLock)
                {
                    if (node.List != null)
                        _accessOrder.Remove(node);
                }
            }
        }

        /// <summary>
        /// 清空全部缓存
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            lock (_evictLock)
                _accessOrder.Clear();
        }

        /// <summary>
        /// 批量清理所有已过期缓存（建议定时/切换页面时调用）
        /// </summary>
        public void CleanExpired()
        {
            var now = DateTime.UtcNow;
            List<string> expiredKeys = new();

            foreach (var pair in _cache)
            {
                if (pair.Value.Value.ExpiresAt <= now)
                    expiredKeys.Add(pair.Key);
            }

            foreach (var k in expiredKeys)
                Remove(k);
        }

        /// <summary>
        /// 将命中节点移至链表尾部，刷新访问时序 + 自动续期过期时间
        /// </summary>
        private void Touch(LinkedListNode<CacheEntry> node)
        {
            lock (_evictLock)
            {
                if (node.List != _accessOrder)
                    return;

                // 访问自动续期过期时间
                node.Value.ExpiresAt = DateTime.UtcNow + _defaultTtl;
                _accessOrder.Remove(node);
                _accessOrder.AddLast(node);
            }
        }

        /// <summary>
        /// 内部统一移除缓存条目（字典+链表同步删除）
        /// </summary>
        private void RemoveInternal(string key, LinkedListNode<CacheEntry> node)
        {
            _cache.TryRemove(key, out _);
            lock (_evictLock)
            {
                if (node.List != null)
                    _accessOrder.Remove(node);
            }
        }

        /// <summary>
        /// 淘汰一条缓存：优先删除过期，无过期则删除最久未使用头部节点
        /// </summary>
        private void EvictOne()
        {
            lock (_evictLock)
            {
                var ptr = _accessOrder.First;
                while (ptr != null)
                {
                    var next = ptr.Next;
                    if (ptr.Value.ExpiresAt <= DateTime.UtcNow)
                    {
                        _cache.TryRemove(ptr.Value.Key, out _);
                        _accessOrder.Remove(ptr);
                        return;
                    }
                    ptr = next;
                }

                // 无过期数据，淘汰LRU表头
                var lruNode = _accessOrder.First;
                if (lruNode != null)
                {
                    _cache.TryRemove(lruNode.Value.Key, out _);
                    _accessOrder.Remove(lruNode);
                }
            }
        }

        /// <summary>
        /// 缓存条目，过期时间支持修改实现访问续期
        /// </summary>
        private sealed class CacheEntry
        {
            public required string Key { get; init; }
            public required T Value { get; init; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}