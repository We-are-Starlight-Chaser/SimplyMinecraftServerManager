using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimplyMinecraftServerManager.Helpers
{
    internal static class ImageCache
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _strongCache = new();
        private static readonly ConcurrentDictionary<string, WeakReference<ImageSource>> _weakCache = new();
        private const int MaxStrongCacheSize = 50;
        
        public static ImageSource? Get(string key)
        {
            if (_strongCache.TryGetValue(key, out var image))
            {
                return image;
            }
            
            if (_weakCache.TryGetValue(key, out var weakRef) && weakRef.TryGetTarget(out var weakImage))
            {
                _strongCache.TryAdd(key, weakImage);
                return weakImage;
            }
            return null;
        }

        public static void Set(string key, ImageSource image)
        {
            _strongCache[key] = image;
            
            if (_strongCache.Count > MaxStrongCacheSize)
            {
                var keysToRemove = _strongCache.Keys.Take(_strongCache.Count - MaxStrongCacheSize).ToList();
                foreach (var oldKey in keysToRemove)
                {
                    _strongCache.TryRemove(oldKey, out _);
                    _weakCache[oldKey] = new WeakReference<ImageSource>(_strongCache.GetValueOrDefault(oldKey)!);
                }
            }
        }

        public static ImageSource GetOrCreate(string key, Func<ImageSource> factory)
        {
            if (Get(key) is ImageSource existing)
                return existing;

            var image = factory();
            Set(key, image);
            return image;
        }
        
        public static void Clear()
        {
            _strongCache.Clear();
            _weakCache.Clear();
        }
    }

    internal static class VisualCache
    {
        private static readonly ConcurrentDictionary<string, WeakReference<Visual>> _cache = new();

        public static T? Get<T>(string key) where T : Visual
        {
            if (_cache.TryGetValue(key, out var weakRef) && weakRef.TryGetTarget(out var visual) && visual is T t)
            {
                return t;
            }
            return null;
        }

        public static void Set<T>(string key, T visual) where T : Visual
        {
            _cache[key] = new WeakReference<Visual>(visual);
        }
    }
}