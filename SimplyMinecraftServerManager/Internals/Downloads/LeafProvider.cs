// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Net.Http;
using System.Text.Json;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// Leaf 服务端提供者。
    /// API 兼容 PaperMC v2 格式。
    /// https://api.leafmc.one/v2/projects/leaf
    /// </summary>
    public class LeafProvider(HttpClient? httpClient = null) : IServerProvider
    {
        private const string BaseUrl = "https://api.leafmc.one/v2/projects/leaf";
        private readonly HttpClient _http = httpClient ?? CreateDefaultClient();

        /// <inheritdoc />
        public ServerPlatform Platform => ServerPlatform.Leaf;

        /// <summary>
        /// 异步获取 Leaf 服务端所有可用的 Minecraft 版本列表。
        /// </summary>
        /// <param name="ct">用于取消操作的令牌。</param>
        /// <returns>一个包含所有可用版本字符串的只读列表，按版本倒序排列。</returns>
        public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
        {
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
        /// 异步获取指定 Minecraft 版本的所有 Leaf 服务端构建信息。
        /// </summary>
        /// <param name="minecraftVersion">要查询构建的 Minecraft 版本。</param>
        /// <param name="ct">用于取消操作的令牌。</param>
        /// <returns>一个包含所有构建信息的只读列表，按构建号倒序排列。</returns>
        public async Task<IReadOnlyList<ServerBuild>> GetBuildsAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            string url = $"{BaseUrl}/versions/{minecraftVersion}/builds";
            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var builds = new List<ServerBuild>();

            foreach (var buildElem in doc.RootElement.GetProperty("builds").EnumerateArray())
            {
                int buildNum = buildElem.GetProperty("build").GetInt32();
                string channel = buildElem.TryGetProperty("channel", out var ch)
                    ? ch.GetString() ?? "default" : "default";

                string fileName = "";
                string? sha256 = null;

                if (buildElem.TryGetProperty("downloads", out var downloads)
                    && downloads.TryGetProperty("application", out var app))
                {
                    fileName = app.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    sha256 = app.TryGetProperty("sha256", out var s) ? s.GetString() : null;
                }

                if (string.IsNullOrEmpty(fileName))
                    fileName = $"leaf-{minecraftVersion}-{buildNum}.jar";

                string downloadUrl =
                    $"{BaseUrl}/versions/{minecraftVersion}/builds/{buildNum}/downloads/{fileName}";

                builds.Add(new ServerBuild
                {
                    Platform = ServerPlatform.Leaf,
                    MinecraftVersion = minecraftVersion,
                    BuildNumber = buildNum,
                    Channel = channel,
                    FileName = fileName,
                    DownloadUrl = downloadUrl,
                    Sha256 = sha256
                });
            }

            builds.Reverse();
            return builds.AsReadOnly();
        }

        /// <summary>
        /// 异步获取指定 Minecraft 版本的最新 Leaf 服务端构建。
        /// </summary>
        /// <param name="minecraftVersion">要查询最新构建的 Minecraft 版本。</param>
        /// <param name="ct">用于取消操作的令牌。</param>
        /// <returns>最新构建信息；若无可用构建则返回 <c>null</c>。</returns>
        public async Task<ServerBuild?> GetLatestBuildAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            var builds = await GetBuildsAsync(minecraftVersion, ct);
            return builds[0];
        }

        /// <summary>
        /// 异步下载指定的 Leaf 服务端构建。
        /// </summary>
        /// <param name="build">要下载的构建信息。</param>
        /// <param name="destinationPath">下载文件的目标路径。</param>
        /// <param name="downloadManager">用于管理下载任务的 <see cref="DownloadManager"/> 实例；为 <c>null</c> 时使用默认实例。</param>
        /// <param name="ct">用于取消操作的令牌。</param>
        /// <returns>新创建的 <see cref="DownloadTask"/> 实例。</returns>
        public async Task<DownloadTask> DownloadAsync(
            ServerBuild build, string destinationPath,
            DownloadManager? downloadManager = null, CancellationToken ct = default)
        {
            var mgr = downloadManager ?? DownloadManager.Default;

            var task = new DownloadTask
            {
                DisplayName = $"Leaf {build.MinecraftVersion} #{build.BuildNumber}",
                Url = build.DownloadUrl,
                DestinationPath = destinationPath,
                ExpectedHash = build.Sha256,
                HashAlgorithm = "SHA256"
            };

            if (ct != default)
                ct.Register(() => task.Cts.Cancel());

            return await mgr.EnqueueAsync(task);
        }

        private static HttpClient CreateDefaultClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SimplyMinecraftServerManager/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }
    }
}