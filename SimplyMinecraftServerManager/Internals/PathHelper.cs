// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 路径辅助工具类，提供应用程序和服务器实例的路径管理功能。
    /// </summary>
    public static class PathHelper
    {
        private static readonly string _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "smsm");

        private static readonly string _configFile = Path.Combine(_root, "config.toml");
        private static readonly string _instancesFile = Path.Combine(_root, "instances.toml");
        private static readonly string _instancesRoot = Path.Combine(_root, "instances");
        private static readonly string _jdksRoot = Path.Combine(_root, "jdks");
        private static readonly string _backupsRoot = Path.Combine(_root, "backups");

        /// <summary>
        /// 获取应用程序根目录路径。
        /// </summary>
        public static string Root => _root;

        /// <summary>
        /// 获取配置文件路径。
        /// </summary>
        public static string ConfigFile => _configFile;

        /// <summary>
        /// 获取实例列表文件路径。
        /// </summary>
        public static string InstancesFile => _instancesFile;

        /// <summary>
        /// 获取服务器实例根目录路径。
        /// </summary>
        public static string InstancesRoot => _instancesRoot;

        /// <summary>
        /// 获取 JDK 安装根目录路径。
        /// </summary>
        public static string JdksRoot => _jdksRoot;

        /// <summary>
        /// 获取备份文件根目录路径。
        /// </summary>
        public static string BackupsRoot => _backupsRoot;
        /// <summary>
        /// 获取指定实例的目录路径。
        /// </summary>
        /// <param name="id">实例 ID。</param>
        /// <returns>实例目录的完整路径。</returns>
        /// <exception cref="ArgumentException">实例 ID 无效时抛出。</exception>
        public static string GetInstanceDir(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(InstancesRoot, id);
        }

        /// <summary>
        /// 获取指定实例的插件/模组目录路径。
        /// </summary>
        /// <param name="id">实例 ID。</param>
        /// <returns>插件/模组目录的完整路径。</returns>
        /// <exception cref="ArgumentException">实例 ID 无效时抛出。</exception>
        public static string GetPluginsDir(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(GetInstanceDir(id), "plugins");
        }

        /// <summary>
        /// 获取指定实例的 Mod 目录路径（Fabric/NeoForge 使用 mods/ 目录）。
        /// </summary>
        /// <param name="id">实例 ID。</param>
        /// <returns>Mod 目录的完整路径。</returns>
        /// <exception cref="ArgumentException">实例 ID 无效时抛出。</exception>
        public static string GetModsDir(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(GetInstanceDir(id), "mods");
        }

        /// <summary>
        /// 获取指定实例的 server.properties 文件路径。
        /// </summary>
        /// <param name="id">实例 ID。</param>
        /// <returns>server.properties 文件的完整路径。</returns>
        /// <exception cref="ArgumentException">实例 ID 无效时抛出。</exception>
        public static string GetServerPropertiesPath(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(GetInstanceDir(id), "server.properties");
        }

        /// <summary>
        /// 获取指定实例的 eula.txt 文件路径。
        /// </summary>
        /// <param name="id">实例 ID。</param>
        /// <returns>eula.txt 文件的完整路径。</returns>
        /// <exception cref="ArgumentException">实例 ID 无效时抛出。</exception>
        public static string GetEulaPath(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(GetInstanceDir(id), "eula.txt");
        }

        /// <summary>
        /// 获取指定实例的服务器 JAR 文件路径。
        /// </summary>
        /// <param name="id">实例 ID。</param>
        /// <param name="jarName">JAR 文件名。</param>
        /// <returns>服务器 JAR 文件的完整路径。</returns>
        /// <exception cref="ArgumentException">实例 ID 或 JAR 文件名无效时抛出。</exception>
        public static string GetServerJarPath(string id, string jarName)
        {
            ValidateInstanceId(id);
            if (string.IsNullOrWhiteSpace(jarName))
                throw new ArgumentException("JAR name cannot be empty", nameof(jarName));

            if (!SecurityHelper.IsValidFileName(jarName))
                throw new ArgumentException("Invalid JAR filename", nameof(jarName));

            string fullPath = Path.Combine(GetInstanceDir(id), jarName);
            return ValidatePathInsideInstance(fullPath, id);
        }

        public static string GetBackupsDir(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(BackupsRoot, id);
        }
        private static void ValidateInstanceId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Instance ID cannot be empty", nameof(id));

            if (SecurityHelper.IsPathTraversal(id))
                throw new ArgumentException("Invalid instance ID", nameof(id));
        }

        /// <summary>
        /// 验证路径是否在指定实例目录内。
        /// </summary>
        /// <param name="path">要验证的路径。</param>
        /// <param name="instanceId">实例 ID。</param>
        /// <returns>规范化后的完整路径。</returns>
        /// <exception cref="InvalidOperationException">路径在实例目录外时抛出。</exception>
        public static string ValidatePathInsideInstance(string path, string instanceId)
        {
            string normalizedPath = Path.GetFullPath(path);
            string normalizedInstanceDir = Path.GetFullPath(GetInstanceDir(instanceId));

            if (!normalizedPath.StartsWith(normalizedInstanceDir + Path.DirectorySeparatorChar) &&
                normalizedPath != normalizedInstanceDir)
            {
                throw new InvalidOperationException("Path is outside instance directory");
            }

            return normalizedPath;
        }

        /// <summary>
        /// 确保所有必需的目录结构存在，如不存在则创建。
        /// </summary>
        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(InstancesRoot);
            Directory.CreateDirectory(JdksRoot);
        }
    }
}