// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using System.Text;
using Tomlyn;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 应用程序配置管理器，负责从 TOML 文件加载和保存全局配置。
    /// </summary>
    public static class ConfigManager
    {
        private static readonly Lock _lock = new();
        private static volatile AppConfig? _cached;

        /// <summary>获取当前配置，若未加载则自动加载</summary>
        public static AppConfig Current
        {
            get
            {
                if (_cached == null) Load();
                return _cached!;
            }
        }

        /// <summary>
        /// 从配置文件加载配置，若文件不存在则创建默认配置。
        /// </summary>
        /// <returns>加载后的 AppConfig 实例</returns>
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
                    DecryptSensitiveFields();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigManager] Failed to load config: {ex.Message}");
                    _cached = new AppConfig();
                    Save(_cached);
                }

                return _cached;
            }
        }

        private static void DecryptSensitiveFields()
        {
            if (_cached == null) return;
            string decrypted = SecureConfig.Decrypt(_cached.DefaultJdkPath);
            if (decrypted != _cached.DefaultJdkPath)
            {
                _cached.DefaultJdkPath = decrypted;
            }
        }

        /// <summary>
        /// 将配置保存到文件。
        /// </summary>
        /// <param name="config">要保存的配置对象</param>
        public static void Save(AppConfig config)
        {
            lock (_lock)
            {
                var configToSave = CloneConfig(config);
                configToSave.DefaultJdkPath = SecureConfig.Encrypt(config.DefaultJdkPath);

                _cached = config;
                PathHelper.EnsureDirectories();
                string toml = Toml.FromModel(configToSave);
                File.WriteAllText(PathHelper.ConfigFile, toml, Encoding.UTF8);
            }
        }

        private static AppConfig CloneConfig(AppConfig original)
        {
            return new AppConfig
            {
                DefaultMinMemoryMb = original.DefaultMinMemoryMb,
                DefaultMaxMemoryMb = original.DefaultMaxMemoryMb,
                DefaultJdkPath = original.DefaultJdkPath,
                DownloadThreads = original.DownloadThreads,
                AutoAcceptEula = original.AutoAcceptEula,
                ConsoleWrapLines = original.ConsoleWrapLines,
                ConsoleFontFamily = original.ConsoleFontFamily,
                ConsoleFontSize = original.ConsoleFontSize,
                Language = original.Language,
                PreferredJdkDistribution = original.PreferredJdkDistribution
            };
        }

        /// <summary>保存当前缓存的配置到文件</summary>
        public static void Save() => Save(Current);
    }
}
