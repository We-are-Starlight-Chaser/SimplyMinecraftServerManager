using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimplyMinecraftServerManager.Internals
{
    public sealed class ServerJarMetadata
    {
        public string ServerType { get; init; } = "未知类型";
        public string MinecraftVersion { get; init; } = "未知版本";
        public bool JarExists { get; init; }
    }

    public static partial class ServerJarMetadataReader
    {
        private sealed record CacheEntry(DateTime LastWriteTimeUtc, ServerJarMetadata Metadata);

        private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static ServerJarMetadata Read(InstanceInfo? info)
        {
            if (info == null)
            {
                return new ServerJarMetadata();
            }

            return Read(info.Id, info.ServerJar);
        }

        public static ServerJarMetadata Read(string instanceId, string serverJar)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(serverJar))
            {
                return new ServerJarMetadata();
            }

            string jarPath;
            try
            {
                jarPath = PathHelper.GetServerJarPath(instanceId, serverJar);
            }
            catch
            {
                return new ServerJarMetadata();
            }

            if (!File.Exists(jarPath))
            {
                return new ServerJarMetadata();
            }

            var fullPath = Path.GetFullPath(jarPath);
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath);

            if (Cache.TryGetValue(fullPath, out var cacheEntry) && cacheEntry.LastWriteTimeUtc == lastWriteTimeUtc)
            {
                return cacheEntry.Metadata;
            }

            var metadata = ReadInternal(fullPath);
            Cache[fullPath] = new CacheEntry(lastWriteTimeUtc, metadata);
            return metadata;
        }

        public static void Invalidate(string instanceId, string serverJar)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(serverJar))
            {
                return;
            }

            try
            {
                var jarPath = PathHelper.GetServerJarPath(instanceId, serverJar);
                Cache.TryRemove(Path.GetFullPath(jarPath), out _);
            }
            catch
            {
            }
        }

        private static ServerJarMetadata ReadInternal(string jarPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(jarPath);
            var detectedType = DetectServerType(fileName);
            var detectedVersion = DetectMinecraftVersion(fileName);

            try
            {
                using var archive = ZipFile.OpenRead(jarPath);
                var manifestText = ReadEntryText(archive, "META-INF/MANIFEST.MF");
                var versionJsonText = ReadEntryText(archive, "version.json");
                var installPropertiesText = ReadEntryText(archive, "install.properties");
                var fabricLaunchProperties = ReadEntryText(archive, "fabric-server-launch.properties");

                if (string.IsNullOrWhiteSpace(detectedType))
                {
                    detectedType =
                        DetectServerType(manifestText) ??
                        DetectServerType(installPropertiesText) ??
                        DetectServerType(fabricLaunchProperties) ??
                        DetectServerTypeFromEntries(archive);
                }

                if (string.IsNullOrWhiteSpace(detectedVersion))
                {
                    detectedVersion =
                        DetectVersionFromVersionJson(versionJsonText) ??
                        DetectMinecraftVersion(manifestText) ??
                        DetectVersionFromInstallProperties(installPropertiesText) ??
                        DetectMinecraftVersion(fabricLaunchProperties);
                }

                if (string.IsNullOrWhiteSpace(detectedType) && !string.IsNullOrWhiteSpace(versionJsonText))
                {
                    detectedType = "vanilla";
                }
            }
            catch
            {
            }

            return new ServerJarMetadata
            {
                JarExists = true,
                ServerType = NormalizeServerType(detectedType),
                MinecraftVersion = string.IsNullOrWhiteSpace(detectedVersion) ? "未知版本" : detectedVersion
            };
        }

        private static string NormalizeServerType(string? serverType)
        {
            if (string.IsNullOrWhiteSpace(serverType))
            {
                return "未知类型";
            }

            return serverType.ToLowerInvariant() switch
            {
                "paper" => "Paper",
                "purpur" => "Purpur",
                "folia" => "Folia",
                "leaves" => "Leaves",
                "leaf" => "Leaf",
                "pufferfish" => "Pufferfish",
                "spigot" => "Spigot",
                "bukkit" => "Bukkit",
                "fabric" => "Fabric",
                "forge" => "Forge",
                "neoforge" => "NeoForge",
                "mohist" => "Mohist",
                "magma" => "Magma",
                "arclight" => "Arclight",
                "vanilla" => "Vanilla",
                _ => serverType
            };
        }

        private static string? DetectServerType(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var lowerText = text.ToLowerInvariant();

            if (lowerText.Contains("purpur")) return "purpur";
            if (lowerText.Contains("folia")) return "folia";
            if (lowerText.Contains("leaves")) return "leaves";
            if (LeafRegex().IsMatch(lowerText)) return "leaf";
            if (lowerText.Contains("pufferfish")) return "pufferfish";
            if (lowerText.Contains("paper")) return "paper";
            if (lowerText.Contains("spigot")) return "spigot";
            if (lowerText.Contains("bukkit")) return "bukkit";
            if (lowerText.Contains("fabric")) return "fabric";
            if (lowerText.Contains("neoforge")) return "neoforge";
            if (lowerText.Contains("forge")) return "forge";
            if (lowerText.Contains("mohist")) return "mohist";
            if (lowerText.Contains("magma")) return "magma";
            if (lowerText.Contains("arclight")) return "arclight";
            if (lowerText.Contains("vanilla")) return "vanilla";

            return null;
        }

        private static string? DetectServerTypeFromEntries(ZipArchive archive)
        {
            string? fallbackType = null;

            foreach (var entry in archive.Entries)
            {
                var detectedType = DetectServerTypeFromEntryName(entry.FullName);
                if (string.IsNullOrWhiteSpace(detectedType))
                {
                    continue;
                }

                // Folia 仍然包含大量 Paper 包路径，必须优先吃掉更具体的特征。
                if (string.Equals(detectedType, "folia", StringComparison.Ordinal))
                {
                    return detectedType;
                }

                fallbackType ??= detectedType;
            }

            return fallbackType;
        }

        private static string? DetectServerTypeFromEntryName(string? entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                return null;
            }

            var lowerName = entryName.Replace('\\', '/').ToLowerInvariant();

            if (lowerName.Contains("folia")) return "folia";
            if (lowerName.Contains("threadedregions")) return "folia";
            if (lowerName.Contains("regionizedserver")) return "folia";
            if (lowerName.Contains("regionscheduler")) return "folia";
            if (lowerName.Contains("entityscheduler")) return "folia";
            if (lowerName.Contains("paperclip")) return "paper";
            if (lowerName.Contains("io/papermc/")) return "paper";
            if (lowerName.Contains("gg/pufferfish/")) return "pufferfish";
            if (lowerName.Contains("org/bukkit/")) return "bukkit";
            if (lowerName.Contains("org/spigotmc/")) return "spigot";
            if (lowerName.Contains("net/fabricmc/")) return "fabric";
            if (lowerName.Contains("net/minecraftforge/")) return "forge";
            if (lowerName.Contains("net/neoforged/")) return "neoforge";
            if (lowerName.Contains("mohist")) return "mohist";
            if (lowerName.Contains("magma")) return "magma";
            if (lowerName.Contains("arclight")) return "arclight";

            return null;
        }

        private static string? DetectMinecraftVersion(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = MinecraftVersionRegex().Match(text);
            return match.Success ? match.Value : null;
        }

        private static string? DetectVersionFromVersionJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("id", out var idElement))
                {
                    return idElement.GetString();
                }
            }
            catch
            {
            }

            return DetectMinecraftVersion(json);
        }

        private static string? DetectVersionFromInstallProperties(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.StartsWith("MC_VERSION=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return line["MC_VERSION=".Length..].Trim();
            }

            return DetectMinecraftVersion(text);
        }

        private static string? ReadEntryText(ZipArchive archive, string entryName)
        {
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(entryName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                return null;
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }

        [GeneratedRegex(@"(?<!\d)(?:1\.\d{1,2}(?:\.\d+)?|\d{2}(?:\.\d+){1,2})(?!\d)")]
        private static partial Regex MinecraftVersionRegex();

        [GeneratedRegex(@"(^|[^a-z])leaf([^a-z]|$)")]
        private static partial Regex LeafRegex();
    }
}
