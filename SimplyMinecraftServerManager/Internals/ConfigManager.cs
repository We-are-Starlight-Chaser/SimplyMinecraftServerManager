using System;
using System.IO;
using System.Text;
using Tomlyn;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 管理 %appdata%/smsm/config.toml 的读写。
    /// </summary>
    public static class ConfigManager
    {
        private static readonly object _lock = new();
        private static AppConfig? _cached;

        /// <summary>当前配置（懒加载）。</summary>
        public static AppConfig Current
        {
            get
            {
                if (_cached == null) Load();
                return _cached!;
            }
        }

        /// <summary>从磁盘加载配置；文件不存在则创建默认配置。</summary>
        public static AppConfig Load()
        {
            lock (_lock)
            {
                PathHelper.EnsureDirectories();

                if (!File.Exists(PathHelper.ConfigFile))
                {
                    _cached = new AppConfig();
                    Save(_cached);
                    return _cached;
                }

                try
                {
                    string toml = File.ReadAllText(PathHelper.ConfigFile, Encoding.UTF8);
                    _cached = Toml.ToModel<AppConfig>(toml);
                }
                catch (Exception)
                {
                    _cached = new AppConfig();
                    Save(_cached);
                }

                return _cached;
            }
        }

        /// <summary>将配置保存到磁盘。</summary>
        public static void Save(AppConfig config)
        {
            lock (_lock)
            {
                _cached = config;
                PathHelper.EnsureDirectories();
                string toml = Toml.FromModel(config);
                File.WriteAllText(PathHelper.ConfigFile, toml, Encoding.UTF8);
            }
        }

        /// <summary>修改后保存当前配置。</summary>
        public static void Save() => Save(Current);
    }
}