// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimplyMinecraftServerManager.Helpers
{
    /// <summary>
    /// 图像缓存类，用于缓存图像源以避免重复加载，使用线程安全的并发字典实现。
    /// </summary>
    internal static class ImageCache
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _cache = new();
        private const int MaxCacheSize = 50;

        /// <summary>
        /// 从缓存中获取指定键的图像源。
        /// </summary>
        /// <param name="key">图像的缓存键。</param>
        /// <returns>缓存中的图像源，如果不存在则返回 null。</returns>
        public static ImageSource? Get(string key)
        {
            _cache.TryGetValue(key, out var image);
            return image;
        }

        /// <summary>
        /// 将图像源存入缓存，如果缓存已满则移除最早的缓存项。
        /// </summary>
        /// <param name="key">图像的缓存键。</param>
        /// <param name="image">要缓存的图像源。</param>
        public static void Set(string key, ImageSource image)
        {
            if (_cache.Count >= MaxCacheSize)
            {
                var keys = _cache.Keys.ToArray();
                int toRemove = Math.Max(1, keys.Length - MaxCacheSize + 1);
                for (int i = 0; i < toRemove && i < keys.Length; i++)
                    _cache.TryRemove(keys[i], out _);
            }
            _cache[key] = image;
        }

        /// <summary>
        /// 从缓存中获取指定键的图像源，如果不存在则通过工厂方法创建并缓存。
        /// </summary>
        /// <param name="key">图像的缓存键。</param>
        /// <param name="factory">用于创建图像源的工厂方法。</param>
        /// <returns>缓存或新创建的图像源。</returns>
        public static ImageSource GetOrCreate(string key, Func<ImageSource> factory)
        {
            if (_cache.TryGetValue(key, out var existing))
                return existing;
            var image = factory();
            Set(key, image);
            return image;
        }

        /// <summary>
        /// 清空图像缓存中的所有项。
        /// </summary>
        public static void Clear() => _cache.Clear();
    }

    /// <summary>
    /// 可视化对象缓存类，使用弱引用缓存 Visual 对象以避免内存泄漏，支持自动回收不可达对象。
    /// </summary>
    internal static class VisualCache
    {
        private static readonly ConcurrentDictionary<string, WeakReference<Visual>> _cache = new();

        /// <summary>
        /// 从缓存中获取指定键的可视化对象。
        /// </summary>
        /// <typeparam name="T">要获取的可视化对象类型。</typeparam>
        /// <param name="key">可视化的缓存键。</param>
        /// <returns>缓存中的可视化对象，如果不存在或已被回收则返回 null。</returns>
        public static T? Get<T>(string key) where T : Visual
        {
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
            => _cache[key] = new WeakReference<Visual>(visual);
    }
}