using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimplyMinecraftServerManager.Helpers
{
    internal static class ImageCache
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _cache = new();
        private const int MaxCacheSize = 50;

        public static ImageSource? Get(string key)
        {
            _cache.TryGetValue(key, out var image);
            return image;
        }

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

        public static ImageSource GetOrCreate(string key, Func<ImageSource> factory)
        {
            if (_cache.TryGetValue(key, out var existing))
                return existing;
            var image = factory();
            Set(key, image);
            return image;
        }

        public static void Clear() => _cache.Clear();
    }

    internal static class VisualCache
    {
        private static readonly ConcurrentDictionary<string, WeakReference<Visual>> _cache = new();

        public static T? Get<T>(string key) where T : Visual
        {
            if (_cache.TryGetValue(key, out var weakRef) && weakRef.TryGetTarget(out var visual) && visual is T t)
                return t;
            return null;
        }

        public static void Set<T>(string key, T visual) where T : Visual
            => _cache[key] = new WeakReference<Visual>(visual);
    }
}