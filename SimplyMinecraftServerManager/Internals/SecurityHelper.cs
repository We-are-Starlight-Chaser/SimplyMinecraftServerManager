using System.IO;
using System.Text.RegularExpressions;

namespace SimplyMinecraftServerManager.Internals
{
    public static partial class SecurityHelper
    {
        private static readonly Regex ValidInstanceNameRegex = ValidInstanceNameRegexPG();

        private static readonly Regex SafePathRegex = SafeRegex();

        public static bool IsValidInstanceName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && ValidInstanceNameRegex.IsMatch(name);
        }

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

        public static string SanitizeInstanceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unnamed";

            name = name.Trim();
            name = Regex.Replace(name, @"[^\a-zA-Z0-9_\-\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af ]", "_");

            if (name.Length > 64) name = name[..64];

            return name;
        }

        public static bool IsPathTraversal(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            path = path.Replace('\\', '/');
            return path.Contains("..") || path.StartsWith('/') || path.Contains(":/") || path.Contains(":\\");
        }

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
        private static partial Regex SafeRegex();
        [GeneratedRegex(@"^[a-zA-Z0-9_\-\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af ]{1,64}$", RegexOptions.Compiled)]
        private static partial Regex ValidInstanceNameRegexPG();
    }
}
