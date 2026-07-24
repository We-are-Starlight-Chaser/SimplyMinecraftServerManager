// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 提供对实例 mods 目录中 Mod JAR 的读取、解析、删除操作。
    /// 通过读取 JAR 内的 fabric.mod.json 获取 Fabric Mod 信息。
    /// </summary>
    public static class ModManager
    {
        private static readonly ConcurrentDictionary<string, (List<ModInfo> Mods, DateTime CacheTime)> _modCache = new();
        private static readonly ConcurrentDictionary<string, (DateTime LastWriteTime, ModInfo Info)> _fileCache = new();
        private static readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(60);

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// 获取指定实例的所有 Mod 信息（包括已禁用的 Mod）。
        /// </summary>
        public static List<ModInfo> GetMods(string instanceId)
        {
            string modsDir = PathHelper.GetModsDir(instanceId);
            if (!Directory.Exists(modsDir))
                return [];

            if (_modCache.TryGetValue(instanceId, out var cached) && (DateTime.UtcNow - cached.CacheTime) < _cacheExpiration)
            {
                return cached.Mods;
            }

            var result = new List<ModInfo>();

            // 扫描 .jar 文件（启用的 Mod）
            foreach (string jarPath in Directory.EnumerateFiles(modsDir, "*.jar"))
            {
                try
                {
                    ModInfo? info = GetOrParseModJar(jarPath);
                    if (info != null)
                        result.Add(info);
                }
                catch (Exception)
                {
                    result.Add(CreateFallbackInfo(jarPath));
                }
            }

            // 扫描禁用的 Mod（.jar.disabled / .jar.off / .jar.old）
            foreach (string disabledPath in Directory.EnumerateFiles(modsDir, "*.jar.*")
                         .Where(p => p.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                                  || p.EndsWith(".off", StringComparison.OrdinalIgnoreCase)
                                  || p.EndsWith(".old", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    ModInfo? info = GetOrParseModJar(disabledPath, true);
                    if (info != null)
                        result.Add(info);
                }
                catch (Exception)
                {
                    result.Add(CreateFallbackInfo(disabledPath, true));
                }
            }

            var sorted = result.OrderBy(m => m.Name).ToList();
            _modCache[instanceId] = (sorted, DateTime.UtcNow);
            return sorted;
        }

        public static void InvalidateCache(string instanceId)
        {
            _modCache.TryRemove(instanceId, out _);
        }

        /// <summary>
        /// 检查文件缓存，仅在 LastWriteTime 变化时重新解析。
        /// </summary>
        private static ModInfo? GetOrParseModJar(string jarPath, bool isDisabled = false)
        {
            var lastWrite = File.GetLastWriteTimeUtc(jarPath);
            string cacheKey = $"{jarPath}|{isDisabled}";

            if (_fileCache.TryGetValue(cacheKey, out var cached) && cached.LastWriteTime == lastWrite)
            {
                return cached.Info;
            }

            ModInfo? info = ParseModJar(jarPath, isDisabled);
            if (info is not null)
            {
                _fileCache[cacheKey] = (lastWrite, info);
            }
            return info;
        }

        /// <summary>
        /// 解析单个 JAR 文件，提取 fabric.mod.json 元数据。
        /// </summary>
        public static ModInfo? ParseModJar(string jarPath, bool isDisabled = false)
        {
            if (!File.Exists(jarPath)) return null;

            using var zip = ZipFile.OpenRead(jarPath);

            // Fabric: fabric.mod.json
            ZipArchiveEntry? entry = zip.GetEntry("fabric.mod.json");

            if (entry == null)
                return CreateFallbackInfo(jarPath, isDisabled);

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            string jsonContent = reader.ReadToEnd();

            return ParseFabricModJson(jsonContent, jarPath, isDisabled);
        }

        /// <summary>
        /// 解析单个 JAR 文件。
        /// </summary>
        public static ModInfo? ParseModJar(string jarPath)
        {
            return ParseModJar(jarPath, false);
        }

        /// <summary>
        /// 删除指定实例中的某个 Mod JAR。
        /// </summary>
        public static bool DeleteMod(string instanceId, string jarFileName)
        {
            if (string.IsNullOrWhiteSpace(jarFileName))
                throw new ArgumentException("Mod file name cannot be empty", nameof(jarFileName));

            if (!SecurityHelper.IsValidFileName(jarFileName))
                throw new ArgumentException("Invalid mod file name", nameof(jarFileName));

            string path = Path.Combine(PathHelper.GetModsDir(instanceId), jarFileName);
            path = Path.GetFullPath(path);

            string modsDir = Path.GetFullPath(PathHelper.GetModsDir(instanceId));
            if (!path.StartsWith(modsDir + Path.DirectorySeparatorChar))
                throw new ArgumentException("Invalid mod path", nameof(jarFileName));

            if (!File.Exists(path))
                return false;

            File.Delete(path);
            InvalidateCache(instanceId);
            return true;
        }

        /// <summary>
        /// 将指定的 Mod JAR 安装到实例的 mods 目录中。
        /// </summary>
        public static ModInfo? InstallMod(string instanceId, string sourceJarPath)
        {
            if (string.IsNullOrWhiteSpace(sourceJarPath))
                throw new ArgumentException("Source JAR path cannot be empty", nameof(sourceJarPath));

            if (!File.Exists(sourceJarPath))
                throw new FileNotFoundException("Source mod JAR not found.", sourceJarPath);

            if (!Path.IsPathRooted(sourceJarPath))
                throw new ArgumentException("Invalid source path", nameof(sourceJarPath));

            sourceJarPath = Path.GetFullPath(sourceJarPath);

            string modsDir = PathHelper.GetModsDir(instanceId);
            Directory.CreateDirectory(modsDir);

            string destPath = Path.Combine(modsDir, Path.GetFileName(sourceJarPath));
            destPath = Path.GetFullPath(destPath);

            string normalizedModsDir = Path.GetFullPath(modsDir);
            if (!destPath.StartsWith(normalizedModsDir + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Destination path is outside mods directory");

            File.Copy(sourceJarPath, destPath, overwrite: true);
            InvalidateCache(instanceId);

            return ParseModJar(destPath);
        }

        // ────────────── 内部解析 ──────────────

        private static ModInfo ParseFabricModJson(string json, string jarPath, bool isDisabled = false)
        {
            var info = new ModInfo
            {
                FileName = Path.GetFileName(jarPath),
                FilePath = Path.GetFullPath(jarPath),
                FileSizeBytes = new FileInfo(jarPath).Length,
                IsDisabled = isDisabled
            };

            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

                var root = doc.RootElement;

                info.Id = GetStringProperty(root, "id");
                info.Name = GetStringProperty(root, "name");
                info.Version = GetStringProperty(root, "version");
                info.Description = GetStringProperty(root, "description");
                info.License = GetStringProperty(root, "license");
                info.Environment = GetStringProperty(root, "environment", "*");
                info.Icon = GetStringProperty(root, "icon");
                info.AccessWidener = GetStringProperty(root, "accessWidener");

                // schemaVersion
                if (root.TryGetProperty("schemaVersion", out var schemaElem) && schemaElem.ValueKind == JsonValueKind.Number)
                {
                    info.SchemaVersion = schemaElem.GetInt32();
                }

                // authors: 支持字符串、字符串数组、对象数组
                if (root.TryGetProperty("authors", out var authorsElem))
                {
                    if (authorsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in authorsElem.EnumerateArray())
                        {
                            string? author = item.ValueKind == JsonValueKind.String
                                ? item.GetString()
                                : item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                            if (!string.IsNullOrEmpty(author))
                                info.Authors.Add(author);
                        }
                    }
                    else if (authorsElem.ValueKind == JsonValueKind.String)
                    {
                        info.Authors.Add(authorsElem.GetString()!);
                    }
                }

                // contributors: 支持字符串、字符串数组、对象数组
                if (root.TryGetProperty("contributors", out var contributorsElem))
                {
                    if (contributorsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in contributorsElem.EnumerateArray())
                        {
                            string? contributor = item.ValueKind == JsonValueKind.String
                                ? item.GetString()
                                : item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                            if (!string.IsNullOrEmpty(contributor))
                                info.Contributors.Add(contributor);
                        }
                    }
                    else if (contributorsElem.ValueKind == JsonValueKind.String)
                    {
                        info.Contributors.Add(contributorsElem.GetString()!);
                    }
                }

                // contact
                if (root.TryGetProperty("contact", out var contactElem) && contactElem.ValueKind == JsonValueKind.Object)
                {
                    info.Contact.Homepage = GetStringProperty(contactElem, "homepage");
                    info.Contact.Sources = GetStringProperty(contactElem, "sources");
                    info.Contact.Issues = GetStringProperty(contactElem, "issues");
                }

                // provides
                if (root.TryGetProperty("provides", out var providesElem) && providesElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in providesElem.EnumerateArray())
                    {
                        string? val = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                        if (!string.IsNullOrEmpty(val))
                            info.Provides.Add(val);
                    }
                }

                // entrypoints
                if (root.TryGetProperty("entrypoints", out var entrypointsElem) && entrypointsElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in entrypointsElem.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                string? val = item.ValueKind == JsonValueKind.String
                                    ? item.GetString()
                                    : item.TryGetProperty("value", out var v) ? v.GetString() : null;
                                if (!string.IsNullOrEmpty(val))
                                    list.Add(val);
                            }
                            info.Entrypoints[prop.Name] = list;
                        }
                    }
                }

                // mixins
                if (root.TryGetProperty("mixins", out var mixinsElem) && mixinsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in mixinsElem.EnumerateArray())
                    {
                        string? val = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                        if (!string.IsNullOrEmpty(val))
                            info.Mixins.Add(val);
                    }
                }

                // jars
                if (root.TryGetProperty("jars", out var jarsElem) && jarsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in jarsElem.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("file", out var fileElem))
                        {
                            string? val = fileElem.ValueKind == JsonValueKind.String ? fileElem.GetString() : null;
                            if (!string.IsNullOrEmpty(val))
                                info.Jars.Add(val);
                        }
                        else if (item.ValueKind == JsonValueKind.String)
                        {
                            info.Jars.Add(item.GetString()!);
                        }
                    }
                }

                // depends
                if (root.TryGetProperty("depends", out var dependsElem) && dependsElem.ValueKind == JsonValueKind.Object)
                {
                    info.Depends = ParseDependencyMap(dependsElem);
                }

                // breaks
                if (root.TryGetProperty("breaks", out var breaksElem) && breaksElem.ValueKind == JsonValueKind.Object)
                {
                    info.Breaks = ParseDependencyMap(breaksElem);
                }

                // custom
                if (root.TryGetProperty("custom", out var customElem) && customElem.ValueKind == JsonValueKind.Object)
                {
                    info.Custom = ParseDependencyMap(customElem);
                }
            }
            catch (Exception)
            {
                // JSON 解析失败时保留文件信息
                if (string.IsNullOrEmpty(info.Name))
                    info.Name = Path.GetFileNameWithoutExtension(jarPath);
            }

            return info;
        }

        private static ModInfo CreateFallbackInfo(string jarPath, bool isDisabled = false)
        {
            return new ModInfo
            {
                Name = Path.GetFileNameWithoutExtension(jarPath),
                FileName = Path.GetFileName(jarPath),
                FilePath = Path.GetFullPath(jarPath),
                FileSizeBytes = File.Exists(jarPath) ? new FileInfo(jarPath).Length : 0,
                IsDisabled = isDisabled
            };
        }

        private static string GetStringProperty(JsonElement root, string propertyName, string defaultValue = "")
        {
            if (root.TryGetProperty(propertyName, out var elem) && elem.ValueKind == JsonValueKind.String)
                return elem.GetString() ?? defaultValue;
            return defaultValue;
        }

        /// <summary>
        /// 解析键值对映射（depends / breaks / custom 等结构相同）。
        /// </summary>
        private static Dictionary<string, object> ParseDependencyMap(JsonElement obj)
        {
            var map = new Dictionary<string, object>();
            foreach (var prop in obj.EnumerateObject())
            {
                object? val = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => prop.Value.GetRawText(),
                    JsonValueKind.Array => prop.Value.GetRawText(),
                    _ => prop.Value.GetRawText(),
                };
                if (val != null)
                    map[prop.Name] = val;
            }
            return map;
        }
    }
}
