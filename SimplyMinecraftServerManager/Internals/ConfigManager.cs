using System.IO;
using System.Text;
using Tomlyn;

namespace SimplyMinecraftServerManager.Internals
{
    public static class ConfigManager
    {
        private static readonly object _lock = new();
        private static AppConfig? _cached;

        public static AppConfig Current
        {
            get
            {
                if (_cached == null) Load();
                return _cached!;
            }
        }

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
                catch (Exception)
                {
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
                Language = original.Language,
                PreferredJdkDistribution = original.PreferredJdkDistribution
            };
        }

        public static void Save() => Save(Current);
    }
}