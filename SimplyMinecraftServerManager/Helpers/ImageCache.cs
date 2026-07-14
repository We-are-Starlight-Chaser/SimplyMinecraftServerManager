// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Windows.Media;

namespace SimplyMinecraftServerManager.Helpers
{
    /// <summary>
    /// 图像缓存类，用于缓存图像源以避免重复加载，使用 LRU 策略的线程安全实现。
    /// </summary>
    internal static class ImageCache
    {
        private static readonly ConcurrentDictionary<string, (ImageSource Image, DateTime LastAccess)> _cache = new();
        private static readonly ConcurrentQueue<string> _accessOrder = new();
        private static readonly Lock _evictionLock = new();
        private const int MaxCacheSize = 50;
        private static readonly TimeSpan ExpirationTime = TimeSpan.FromMinutes(10);

        /// <summary>
        /// 从缓存中获取指定键的图像源。
        /// </summary>
        /// <param name="key">图像的缓存键。</param>
        /// <returns>缓存中的图像源，如果不存在则返回 null。</returns>
        public static ImageSource? Get(string key)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                // 未过期：更新访问时间、入队时序
                if (DateTime.UtcNow - entry.LastAccess < ExpirationTime)
                {
                    _cache[key] = (entry.Image, DateTime.UtcNow);
                    _accessOrder.Enqueue(key);
                    return entry.Image;
                }
                // 过期直接删除，不再入队无效key
                _cache.TryRemove(key, out _);
            }
            return null;
        }

        /// <summary>
        /// 将图像源存入缓存，如果缓存已满则移除最近最少使用的缓存项。
        /// </summary>
        /// <param name="key">图像的缓存键。</param>
        /// <param name="image">要缓存的图像源。</param>
        public static void Set(string key, ImageSource image)
        {
            // 存在旧key，先更新时间并记录访问，不新增重复队列节点
            if (_cache.ContainsKey(key))
            {
                _cache[key] = (image, DateTime.UtcNow);
                _accessOrder.Enqueue(key);
                return;
            }

            // 容量超限循环淘汰空位
            while (_cache.Count >= MaxCacheSize)
            {
                EvictOldest();
            }

            _cache[key] = (image, DateTime.UtcNow);
            _accessOrder.Enqueue(key);
        }

        private static void EvictOldest()
        {
            lock (_evictionLock)
            {
                // 持续弹出，直到找到真实存在的缓存项删除
                while (_accessOrder.TryDequeue(out var candidate))
                {
                    if (_cache.TryRemove(candidate, out _))
                        return;
                    // 已过期/已删除的无效key直接丢弃，继续下一个
                }
            }
        }

        /// <summary>
        /// 从缓存中获取指定键的图像源，如果不存在则通过工厂方法创建并缓存。
        /// </summary>
        /// <param name="key">图像的缓存键。</param>
        /// <param name="factory">用于创建图像源的工厂方法。</param>
        /// <returns>缓存或新创建的图像源。</returns>
        public static ImageSource GetOrCreate(string key, Func<ImageSource> factory)
        {
            var existing = Get(key);
            if (existing != null) return existing;
            var image = factory();
            Set(key, image);
            return image;
        }

        /// <summary>
        /// 清空图像缓存中的所有项。
        /// </summary>
        public static void Clear()
        {
            _cache.Clear();
            lock (_evictionLock)
            {
                while (_accessOrder.TryDequeue(out _)) { }
            }
        }

        /// <summary>
        /// 清理已过期的缓存项。
        /// </summary>
        public static void CleanupExpired()
        {
            var now = DateTime.UtcNow;
            var expiredKeys = new List<string>();
            foreach (var kvp in _cache)
            {
                if (now - kvp.Value.LastAccess >= ExpirationTime)
                    expiredKeys.Add(kvp.Key);
            }

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }

            // 清空队列所有冗余无效key，彻底释放内存
            lock (_evictionLock)
            {
                while (_accessOrder.TryDequeue(out _)) { }
            }
        }
    }

    /// <summary>
    /// 可视化对象缓存类，使用弱引用缓存 Visual 对象以避免内存泄漏，支持自动回收不可达对象。
    /// </summary>
    internal static class VisualCache
    {
        private static readonly ConcurrentDictionary<string, WeakReference<Visual>> _cache = new();
        private static int _cleanupCounter;
        private const int CleanupInterval = 50;
        // 增加缓存上限，防止无限膨胀
        private const int MaxVisualCacheCount = 200;

        /// <summary>
        /// 从缓存中获取指定键的可视化对象。
        /// </summary>
        /// <typeparam name="T">要获取的可视化对象类型。</typeparam>
        /// <param name="key">可视化的缓存键。</param>
        /// <returns>缓存中的可视化对象，如果不存在或已被回收则返回 null。</returns>
        public static T? Get<T>(string key) where T : Visual
        {
            // 每次读取顺带触发一次清理
            Cleanup();
            if (_cache.TryGetValue(key, out var weakRef) && weakRef.TryGetTarget(out var visual) && visual is T t)
                return t;
            return null;
        }

        /// <summary>
        /// 将可视化对象存入缓存，使用弱引用以允许垃圾回收器在需要时回收对象。
        /// </summary>
        /// <typeparam name="T">要缓存的可视化对象类型。</typeparam>
        /// <param name="key">可视化的缓存键。</param>
        /// <param name="visual">要缓存的可视化对象。</param>
        public static void Set<T>(string key, T visual) where T : Visual
        {
            // 存入前先清理失效项，控制总量
            Cleanup();
            // 超上限则随机移除一条旧数据
            if (_cache.Count >= MaxVisualCacheCount)
            {
                var first = _cache.Keys.FirstOrDefault();
                if (first != null)
                    _cache.TryRemove(first, out _);
            }
            _cache[key] = new WeakReference<Visual>(visual);
        }

        /// <summary>
        /// 主动强制清理所有失效弱引用条目
        /// </summary>
        public static void ForceCleanup()
        {
            var deadKeys = new List<string>();
            foreach (var kvp in _cache)
            {
                if (!kvp.Value.TryGetTarget(out _))
                    deadKeys.Add(kvp.Key);
            }
            foreach (var key in deadKeys)
            {
                _cache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 清理已被垃圾回收器回收的弱引用条目。
        /// </summary>
        public static void Cleanup()
        {
            if (Interlocked.Increment(ref _cleanupCounter) % CleanupInterval != 0)
                return;
            ForceCleanup();
        }

        /// <summary>
        /// 清空全部可视化缓存
        /// </summary>
        public static void Clear()
        {
            _cache.Clear();
        }
    }
}