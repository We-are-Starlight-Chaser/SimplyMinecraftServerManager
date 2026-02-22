using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 提供对实例 plugins 目录中插件 JAR 的读取、解析、删除操作。
    /// 通过读取 JAR 内的 plugin.yml / paper-plugin.yml 获取插件信息。
    /// </summary>
    public static class PluginManager
    {
        // YamlDotNet 反序列化器（线程安全，可复用）
        private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        /// <summary>
        /// 获取指定实例的所有插件信息。
        /// </summary>
        public static List<PluginInfo> GetPlugins(string instanceId)
        {
            string pluginsDir = PathHelper.GetPluginsDir(instanceId);
            if (!Directory.Exists(pluginsDir))
                return new List<PluginInfo>();

            var result = new List<PluginInfo>();

            foreach (string jarPath in Directory.EnumerateFiles(pluginsDir, "*.jar"))
            {
                try
                {
                    PluginInfo? info = ParsePluginJar(jarPath);
                    if (info != null)
                        result.Add(info);
                }
                catch (Exception)
                {
                    // JAR 损坏或无法读取，创建一个仅包含文件信息的占位条目
                    result.Add(CreateFallbackInfo(jarPath));
                }
            }

            return result.OrderBy(p => p.Name).ToList();
        }

        /// <summary>
        /// 解析单个 JAR 文件，提取 plugin.yml 元数据。
        /// </summary>
        public static PluginInfo? ParsePluginJar(string jarPath)
        {
            if (!File.Exists(jarPath)) return null;

            using var zip = ZipFile.OpenRead(jarPath);

            // 依次尝试 plugin.yml（Bukkit/Spigot）和 paper-plugin.yml（Paper）
            ZipArchiveEntry? entry = zip.GetEntry("plugin.yml")
                                    ?? zip.GetEntry("paper-plugin.yml");

            if (entry == null)
                return CreateFallbackInfo(jarPath);

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            string yamlContent = reader.ReadToEnd();

            return ParsePluginYaml(yamlContent, jarPath);
        }

        /// <summary>
        /// 删除指定实例中的某个插件 JAR。
        /// </summary>
        /// <param name="instanceId">实例 UUID</param>
        /// <param name="jarFileName">JAR 文件名（如 "EssentialsX-2.20.jar"）</param>
        /// <returns>是否成功删除</returns>
        public static bool DeletePlugin(string instanceId, string jarFileName)
        {
            // 安全检查：防止路径遍历攻击
            if (jarFileName.Contains("..") || jarFileName.Contains(Path.DirectorySeparatorChar)
                || jarFileName.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException("Invalid plugin file name.", nameof(jarFileName));
            }

            string path = Path.Combine(PathHelper.GetPluginsDir(instanceId), jarFileName);

            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }

        /// <summary>
        /// 将插件 JAR 复制到实例的 plugins 目录。
        /// </summary>
        public static PluginInfo? InstallPlugin(string instanceId, string sourceJarPath)
        {
            if (!File.Exists(sourceJarPath))
                throw new FileNotFoundException("Source plugin JAR not found.", sourceJarPath);

            string pluginsDir = PathHelper.GetPluginsDir(instanceId);
            Directory.CreateDirectory(pluginsDir);

            string destPath = Path.Combine(pluginsDir, Path.GetFileName(sourceJarPath));
            File.Copy(sourceJarPath, destPath, overwrite: true);

            return ParsePluginJar(destPath);
        }

        // ────────────── 内部解析 ──────────────

        private static PluginInfo ParsePluginYaml(string yaml, string jarPath)
        {
            var info = new PluginInfo
            {
                FileName = Path.GetFileName(jarPath),
                FilePath = Path.GetFullPath(jarPath),
                FileSizeBytes = new FileInfo(jarPath).Length
            };

            try
            {
                // 反序列化为字典以灵活处理各种格式差异
                var dict = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yaml);
                if (dict == null)
                    return info;

                info.Name = GetString(dict, "name");
                info.Version = GetString(dict, "version");
                info.Description = GetString(dict, "description");
                info.MainClass = GetString(dict, "main");
                info.ApiVersion = GetString(dict, "api-version");

                // authors（列表）优先于 author（单值）
                info.Authors = GetStringList(dict, "authors");
                if (info.Authors.Count == 0)
                {
                    string singleAuthor = GetString(dict, "author");
                    if (!string.IsNullOrEmpty(singleAuthor))
                        info.Authors.Add(singleAuthor);
                }

                info.Dependencies = GetStringList(dict, "depend");
                info.SoftDependencies = GetStringList(dict, "softdepend");
            }
            catch (Exception)
            {
                // YAML 解析失败时保留文件信息
                if (string.IsNullOrEmpty(info.Name))
                    info.Name = Path.GetFileNameWithoutExtension(jarPath);
            }

            return info;
        }

        private static PluginInfo CreateFallbackInfo(string jarPath)
        {
            return new PluginInfo
            {
                Name = Path.GetFileNameWithoutExtension(jarPath),
                FileName = Path.GetFileName(jarPath),
                FilePath = Path.GetFullPath(jarPath),
                FileSizeBytes = File.Exists(jarPath) ? new FileInfo(jarPath).Length : 0
            };
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            return dict.TryGetValue(key, out object? value) ? value?.ToString() ?? "" : "";
        }

        private static List<string> GetStringList(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out object? value))
                return new List<string>();

            if (value is IList<object> list)
                return list.Select(o => o?.ToString() ?? "").Where(s => s.Length > 0).ToList();

            // 有时写成单个字符串
            string? s = value?.ToString();
            return string.IsNullOrEmpty(s) ? new List<string>() : new List<string> { s };
        }
    }
}