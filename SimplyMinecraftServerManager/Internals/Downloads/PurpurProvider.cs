// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Net.Http;
using System.Text.Json;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// Purpur 服务端提供者。
    /// https://api.purpurmc.org/v2/purpur
    /// </summary>
    public class PurpurProvider(HttpClient? httpClient = null) : IServerProvider
    {
        private const string BaseUrl = "https://api.purpurmc.org/v2/purpur";
        private readonly HttpClient _http = httpClient ?? CreateDefaultClient();

        /// <summary>
        /// Purpur 平台标识。
        /// </summary>
        public ServerPlatform Platform => ServerPlatform.Purpur;

        /// <summary>
        /// 获取所有支持的 Minecraft 版本列表（降序排列）。
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>版本号只读列表</returns>
        public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
        {
            // GET /v2/purpur → { "versions": ["1.14.1", ..., "1.21.4"] }
            string json = await _http.GetStringAsync(BaseUrl, ct);
            using var doc = JsonDocument.Parse(json);

            var versions = doc.RootElement
                .GetProperty("versions")
                .EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();

            versions.Reverse();
            return versions.AsReadOnly();
        }

        /// <summary>
        /// 获取指定 Minecraft 版本的所有构建（降序排列，最新在前）。
        /// </summary>
        /// <param name="minecraftVersion">Minecraft 版本号</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>构建列表只读集合</returns>
        public async Task<IReadOnlyList<ServerBuild>> GetBuildsAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            // GET /v2/purpur/{version}
            string url = $"{BaseUrl}/{minecraftVersion}";
            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var allBuilds = doc.RootElement
                .GetProperty("builds")
                .GetProperty("all")
                .EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();

            string? latest = doc.RootElement
                .GetProperty("builds")
                .GetProperty("latest")
                .GetString();

            var builds = new List<ServerBuild>();

            foreach (string b in allBuilds)
            {
                if (!int.TryParse(b, out int buildNum)) continue;

                string fileName = $"purpur-{minecraftVersion}-{buildNum}.jar";
                string downloadUrl = $"{BaseUrl}/{minecraftVersion}/{buildNum}/download";

                builds.Add(new ServerBuild
                {
                    Platform = ServerPlatform.Purpur,
                    MinecraftVersion = minecraftVersion,
                    BuildNumber = buildNum,
                    Channel = "default",
                    FileName = fileName,
                    DownloadUrl = downloadUrl,
                    Sha256 = null // Purpur API 不在列表中返回哈希
                });
            }

            builds.Reverse();
            return builds.AsReadOnly();
        }

        /// <summary>
        /// 获取指定 Minecraft 版本的最新构建，并尝试获取 MD5 哈希。
        /// </summary>
        /// <param name="minecraftVersion">Minecraft 版本号</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>最新构建，无可用构建时返回 null</returns>
        public async Task<ServerBuild?> GetLatestBuildAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            // GET /v2/purpur/{version}
            string url = $"{BaseUrl}/{minecraftVersion}";
            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            string? latestStr = doc.RootElement
                .GetProperty("builds")
                .GetProperty("latest")
                .GetString();

            if (latestStr == null || !int.TryParse(latestStr, out int buildNum))
                return null;

            // 尝试获取 md5 哈希
            string? md5 = null;
            try
            {
                string buildUrl = $"{BaseUrl}/{minecraftVersion}/{buildNum}";
                string buildJson = await _http.GetStringAsync(buildUrl, ct);
                using var buildDoc = JsonDocument.Parse(buildJson);
                md5 = buildDoc.RootElement.TryGetProperty("md5", out var m)
                    ? m.GetString() : null;
            }
            catch { /* optional */ }

            string fileName = $"purpur-{minecraftVersion}-{buildNum}.jar";
            string downloadUrl = $"{BaseUrl}/{minecraftVersion}/{buildNum}/download";

            return new ServerBuild
            {
                Platform = ServerPlatform.Purpur,
                MinecraftVersion = minecraftVersion,
                BuildNumber = buildNum,
                Channel = "default",
                FileName = fileName,
                DownloadUrl = downloadUrl,
                Sha256 = null
            };
        }

        /// <summary>
        /// 下载指定构建到目标路径。
        /// </summary>
        /// <param name="build">要下载的构建</param>
        /// <param name="destinationPath">保存的本地完整路径</param>
        /// <param name="downloadManager">可选下载管理器，为 null 时使用全局默认实例</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>下载任务</returns>
        public async Task<DownloadTask> DownloadAsync(
            ServerBuild build, string destinationPath,
            DownloadManager? downloadManager = null, CancellationToken ct = default)
        {
            var mgr = downloadManager ?? DownloadManager.Default;

            var task = new DownloadTask
            {
                DisplayName = $"Purpur {build.MinecraftVersion} #{build.BuildNumber}",
                Url = build.DownloadUrl,
                DestinationPath = destinationPath,
                ExpectedHash = build.Sha256,
                HashAlgorithm = "SHA256"
            };

            if (ct != default)
                ct.Register(() => task.Cts.Cancel());

            return await mgr.EnqueueAsync(task);
        }

        /// <summary>
        /// 创建默认的 HttpClient 实例，超时时间 30 分钟。
        /// </summary>
        private static HttpClient CreateDefaultClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SimplyMinecraftServerManager/1.0 (https://github.com/We-are-Starlight-Chaser/SimplyMinecraftServerManager)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }
    }
}