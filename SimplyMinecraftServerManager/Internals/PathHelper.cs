using System;
using System.IO;

namespace SimplyMinecraftServerManager.Internals
{
    public static class PathHelper
    {
        private static readonly string _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "smsm");

        public static string Root => _root;
        public static string ConfigFile => Path.Combine(_root, "config.toml");
        public static string InstancesFile => Path.Combine(_root, "instances.toml");
        public static string InstancesRoot => Path.Combine(_root, "instances");

        /// <summary>%appdata%/smsm/jdks/ — 自动下载的 JDK 安装目录</summary>
        public static string JdksRoot => Path.Combine(_root, "jdks");

        public static string GetInstanceDir(string id)
            => Path.Combine(InstancesRoot, id);

        public static string GetPluginsDir(string id)
            => Path.Combine(GetInstanceDir(id), "plugins");

        public static string GetServerPropertiesPath(string id)
            => Path.Combine(GetInstanceDir(id), "server.properties");

        public static string GetEulaPath(string id)
            => Path.Combine(GetInstanceDir(id), "eula.txt");

        public static string GetServerJarPath(string id, string jarName)
            => Path.Combine(GetInstanceDir(id), jarName);

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(InstancesRoot);
            Directory.CreateDirectory(JdksRoot);
        }
    }
}