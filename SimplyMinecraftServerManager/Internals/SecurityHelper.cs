// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using System.Text.RegularExpressions;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 安全辅助工具类，提供输入验证、路径安全检查和清理功能。
    /// </summary>
    public static partial class SecurityHelper
    {
        private static readonly Regex ValidInstanceNameRegex = ValidInstanceName();

        private static readonly Regex SafePathRegex = SafePath();

        /// <summary>
        /// 验证实例名称是否有效。
        /// </summary>
        /// <param name="name">要验证的实例名称。</param>
        /// <returns>如果名称有效则返回 true，否则返回 false。</returns>
        public static bool IsValidInstanceName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && ValidInstanceNameRegex.IsMatch(name);
        }

        /// <summary>
        /// 验证文件名是否有效且安全。
        /// </summary>
        /// <param name="fileName">要验证的文件名。</param>
        /// <returns>如果文件名有效则返回 true，否则返回 false。</returns>
        public static bool IsValidFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            if (fileName.Length > 255) return false;

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in fileName)
            {
                if (Array.IndexOf(invalidChars, c) >= 0) return false;
            }

            return SafePathRegex.IsMatch(fileName);
        }

        /// <summary>
        /// 验证 JVM 启动参数是否安全。
        /// </summary>
        /// <param name="args">要验证的 JVM 参数字符串。</param>
        /// <returns>如果参数安全则返回 true，否则返回 false。</returns>
        public static bool IsValidJvmArgs(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return true;
            if (args.Length > 4096) return false;

            string[] dangerousPatterns =
            [
                "--add-opens",
                "--add-exports",
                "-XX:+IgnoreUnrecognizedVMOptions",
                "-Djava.ext.dirs",
                "-Djava.class.path",
                "--module-path",
                "-cp ",
                "-classpath"
            ];

            string lowerArgs = args.ToLowerInvariant();
            foreach (var pattern in dangerousPatterns)
            {
                if (lowerArgs.Contains(pattern, StringComparison.InvariantCultureIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 清理实例名称，移除不安全字符并截断至最大长度。
        /// </summary>
        /// <param name="name">要清理的实例名称。</param>
        /// <returns>清理后的安全实例名称。</returns>
        public static string SanitizeInstanceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unnamed";

            name = name.Trim();
            name = SanitizeInstanceName().Replace(name, "_");

            if (name.Length > 64) name = name[..64];

            return name;
        }

        /// <summary>
        /// 检测路径是否存在目录遍历攻击。
        /// </summary>
        /// <param name="path">要检查的路径。</param>
        /// <returns>如果检测到目录遍历则返回 true，否则返回 false。</returns>
        public static bool IsPathTraversal(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            path = path.Replace('\\', '/');
            return path.Contains("..") || path.StartsWith('/') || path.Contains(":/") || path.Contains(":\\");
        }

        /// <summary>
        /// 验证并规范化路径，确保路径在指定的基础目录内。
        /// </summary>
        /// <param name="inputPath">要验证的输入路径。</param>
        /// <param name="baseDirectory">基础目录路径。</param>
        /// <returns>规范化后的完整路径。</returns>
        /// <exception cref="ArgumentException">路径为空或存在目录遍历时抛出。</exception>
        public static string ValidateAndNormalizePath(string inputPath, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("Path cannot be empty", nameof(inputPath));

            if (IsPathTraversal(inputPath))
                throw new ArgumentException("Path traversal detected", nameof(inputPath));

            string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, inputPath));
            string normalizedBase = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar);

            if (!fullPath.StartsWith(normalizedBase + Path.DirectorySeparatorChar) && fullPath != normalizedBase)
                throw new ArgumentException("Path outside base directory", nameof(inputPath));

            return fullPath;
        }

        [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled)]
        private static partial Regex SafePath();
        [GeneratedRegex(@"^[a-zA-Z0-9_\-\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af ]{1,64}$", RegexOptions.Compiled)]
        private static partial Regex ValidInstanceName();
        [GeneratedRegex(@"[^a-zA-Z0-9_\-\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af ]")]
        private static partial Regex SanitizeInstanceName();
    }
}
