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

        public static string JdksRoot => Path.Combine(_root, "jdks");

        public static string GetInstanceDir(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(InstancesRoot, id);
        }

        public static string GetPluginsDir(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(GetInstanceDir(id), "plugins");
        }

        public static string GetServerPropertiesPath(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(GetInstanceDir(id), "server.properties");
        }

        public static string GetEulaPath(string id)
        {
            ValidateInstanceId(id);
            return Path.Combine(GetInstanceDir(id), "eula.txt");
        }

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

        private static void ValidateInstanceId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Instance ID cannot be empty", nameof(id));

            if (SecurityHelper.IsPathTraversal(id))
                throw new ArgumentException("Invalid instance ID", nameof(id));
        }

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

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(InstancesRoot);
            Directory.CreateDirectory(JdksRoot);
        }
    }
}