// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using SimplyMinecraftServerManager.Helpers;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// Fabric Meta API 提供者 (meta.fabricmc.net)。
    /// 通过 Fabric 官方 API 获取 Loader 版本和服务器 JAR 下载链接。
    /// </summary>
    public class FabricMetaProvider(HttpClient? httpClient = null) : IServerProvider
    {
        private const string MetaBaseUrl = "https://meta.fabricmc.net/v2";
        private readonly HttpClient _http = httpClient ?? HttpHelper.CreateDefaultClient();
        private static readonly MemoryCache<IReadOnlyList<string>> _versionsCache = new(TimeSpan.FromMinutes(5), 10);

        public ServerPlatform Platform => ServerPlatform.Fabric;

        /// <summary>
        /// 获取所有支持的 Minecraft 版本列表（降序）。
        /// API: GET /versions/game
        /// </summary>
        public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
        {
            string cacheKey = "fabric:versions";
            if (_versionsCache.TryGet(cacheKey, out var cached))
                return cached;

            string json = await _http.GetStringAsync($"{MetaBaseUrl}/versions/game", ct);
            using var doc = JsonDocument.Parse(json);

            var versions = doc.RootElement.EnumerateArray()
                .Select(e => e.GetProperty("version").GetString()!)
                .Where(v => !string.IsNullOrEmpty(v))
                .OrderByDescending(v => ParseComparableVersion(v))
                .ToList();

            var result = versions.AsReadOnly();
            _versionsCache.Set(cacheKey, result);
            return result;
        }

        /// <summary>
        /// 获取指定 MC 版本的所有构建（每个 Loader 版本为一个构建）。
        /// API: GET /versions/loader/{minecraftVersion}
        /// </summary>
        public async Task<IReadOnlyList<ServerBuild>> GetBuildsAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            string url = $"{MetaBaseUrl}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}";
            Debug.WriteLine($"[FabricMeta] 获取构建列表: {url}");
            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            // 获取最新 installer 版本（用于下载服务端 JAR）
            string installerVersion = await GetLatestInstallerVersionAsync(ct);
            Debug.WriteLine($"[FabricMeta] Installer 版本: {installerVersion}");

            var builds = new List<ServerBuild>();
            int buildIndex = 0;

            foreach (var loaderEntry in doc.RootElement.EnumerateArray())
            {
                string loaderVersion = loaderEntry.TryGetProperty("loader", out var loader)
                    && loader.TryGetProperty("version", out var lv) ? lv.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(loaderVersion))
                    continue;

                // Fabric 服务端 JAR 端点要求 loader >= 0.12，否则返回 400
                if (!IsLoaderVersionSupported(loaderVersion))
                {
                    Debug.WriteLine($"[FabricMeta] 跳过不支持的 loader 版本: {loaderVersion} (< 0.12)");
                    continue;
                }

                // Fabric 服务端 JAR 下载: /versions/loader/{mc}/{loader}/{installer}/server/jar
                // 必须包含 installer_version 参数，否则返回 404
                string installerUrl = $"{MetaBaseUrl}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}/{Uri.EscapeDataString(loaderVersion)}/{Uri.EscapeDataString(installerVersion)}/server/jar";
                string fileName = $"fabric-server-mc.{minecraftVersion}-loader.{loaderVersion}-launcher.{installerVersion}.jar";

                Debug.WriteLine($"[FabricMeta] 构建 #{buildIndex}: loader={loaderVersion}, URL={installerUrl}");

                builds.Add(new ServerBuild
                {
                    Platform = ServerPlatform.Fabric,
                    MinecraftVersion = minecraftVersion,
                    BuildNumber = buildIndex++,
                    Channel = loaderVersion,
                    FileName = fileName,
                    DownloadUrl = installerUrl,
                    Sha256 = null,
                });
            }

            Debug.WriteLine($"[FabricMeta] 共找到 {builds.Count} 个构建");
            return builds.AsReadOnly();
        }

        /// <summary>
        /// 获取最新的 Fabric installer 版本。
        /// API: GET /versions/installer
        /// </summary>
        private async Task<string> GetLatestInstallerVersionAsync(CancellationToken ct = default)
        {
            const string cacheKey = "fabric:installer:latest";
            if (_versionsCache.TryGet(cacheKey, out var cached) && cached.Count > 0)
                return cached[0];

            string url = $"{MetaBaseUrl}/versions/installer";
            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            // 第一个元素是最新版本
            string latestVersion = doc.RootElement.EnumerateArray()
                .FirstOrDefault()
                .TryGetProperty("version", out var v) ? v.GetString() ?? "1.1.1" : "1.1.1";

            _versionsCache.Set(cacheKey, new[] { latestVersion }.AsReadOnly());
            return latestVersion;
        }

        /// <summary>
        /// 获取指定 MC 版本的最新构建（最新 Loader 版本）。
        /// </summary>
        public async Task<ServerBuild?> GetLatestBuildAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            var builds = await GetBuildsAsync(minecraftVersion, ct);
            return builds.Count > 0 ? builds[0] : null;
        }

        /// <summary>
        /// 下载指定构建到目标路径。
        /// </summary>
        public async Task<DownloadTask> DownloadAsync(
            ServerBuild build, string destinationPath,
            DownloadManager? downloadManager = null, CancellationToken ct = default)
        {
            var mgr = downloadManager ?? DownloadManager.Default;

            Debug.WriteLine($"[FabricMeta] 开始下载任务: MC={build.MinecraftVersion}, Loader={build.Channel}");
            Debug.WriteLine($"[FabricMeta] 下载 URL: {build.DownloadUrl}");
            Debug.WriteLine($"[FabricMeta] 目标路径: {destinationPath}");

            var task = new DownloadTask
            {
                DisplayName = $"Fabric {build.MinecraftVersion} (loader {build.Channel})",
                Url = build.DownloadUrl,
                DestinationPath = destinationPath,
                ExpectedHash = build.Sha256,
                HashAlgorithm = "SHA256",
            };

            if (ct != default)
                ct.Register(() => task.Cts.Cancel());

            return await mgr.EnqueueAsync(task);
        }

        private static bool IsLoaderVersionSupported(string version)
        {
            // Fabric 服务端无交互安装要求 loader >= 0.12
            int separatorIndex = version.IndexOfAny(['-', '+']);
            string normalized = separatorIndex >= 0 ? version[..separatorIndex] : version;
            string[] parts = normalized.Split('.');
            if (parts.Length >= 2
                && int.TryParse(parts[0], out int major)
                && int.TryParse(parts[1], out int minor))
            {
                return major > 0 || minor >= 12;
            }
            return true;
        }

        private static Version ParseComparableVersion(string version)
        {
            string normalized = version;
            int separatorIndex = normalized.IndexOfAny(['-', '+']);
            if (separatorIndex >= 0)
                normalized = normalized[..separatorIndex];

            return Version.TryParse(normalized, out var parsed) ? parsed : new Version(0, 0);
        }
    }
}
