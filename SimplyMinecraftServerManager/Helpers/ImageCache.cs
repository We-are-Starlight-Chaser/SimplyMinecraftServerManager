using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimplyMinecraftServerManager.Helpers
{
    internal static class ImageCache
    {
        private static readonly ConcurrentDictionary<string, WeakReference<ImageSource>> _cache = new();
        
        public static ImageSource? Get(string key)
        {
            if (_cache.TryGetValue(key, out var weakRef) && weakRef.TryGetTarget(out var image))
            {
                return image;
            }
            return null;
        }

        public static void Set(string key, ImageSource image)
        {
            _cache[key] = new WeakReference<ImageSource>(image);
        }

        public static ImageSource GetOrCreate(string key, Func<ImageSource> factory)
        {
            if (Get(key) is ImageSource existing)
                return existing;

            var image = factory();
            Set(key, image);
            return image;
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