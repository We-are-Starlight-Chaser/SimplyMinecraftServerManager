// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Net.Http;
using System.Text.Json;
using SimplyMinecraftServerManager.Helpers;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// PaperMC v2 API 格式的通用服务端提供者。
    /// 用于 Leaf、Leaves 等使用 v2 API 的平台。
    /// </summary>
    public sealed class PaperV2ApiProvider : IServerProvider
    {
        private readonly string _baseUrl;
        private readonly string _cachePrefix;
        private readonly string _defaultFileNamePrefix;
        private readonly HttpClient _http;
        private readonly MemoryCache<IReadOnlyList<string>> _versionsCache = new(TimeSpan.FromMinutes(5), 10);

        /// <inheritdoc />
        public ServerPlatform Platform { get; }

        /// <summary>
        /// 创建 PaperMC v2 API 提供者。
        /// </summary>
        /// <param name="platform">平台枚举</param>
        /// <param name="baseUrl">API 基础 URL</param>
        /// <param name="cachePrefix">缓存键前缀</param>
        /// <param name="defaultFileNamePrefix">默认 JAR 文件名前缀</param>
        /// <param name="httpClient">可选共享 HttpClient</param>
        public PaperV2ApiProvider(
            ServerPlatform platform,
            string baseUrl,
            string cachePrefix,
            string defaultFileNamePrefix,
            HttpClient? httpClient = null)
        {
            Platform = platform;
            _baseUrl = baseUrl;
            _cachePrefix = cachePrefix;
            _defaultFileNamePrefix = defaultFileNamePrefix;
            _http = httpClient ?? HttpHelper.CreateDefaultClient();
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
        {
            string cacheKey = $"{_cachePrefix}:versions";
            if (_versionsCache.TryGet(cacheKey, out var cached))
                return cached;

            string json = await _http.GetStringAsync(_baseUrl, ct);
            using var doc = JsonDocument.Parse(json);

            var versions = doc.RootElement
                .GetProperty("versions")
                .EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();

            versions.Reverse();
            var result = versions.AsReadOnly();
            _versionsCache.Set(cacheKey, result);
            return result;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ServerBuild>> GetBuildsAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            string url = $"{_baseUrl}/versions/{minecraftVersion}/builds";
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
                    fileName = $"{_defaultFileNamePrefix}-{minecraftVersion}-{buildNum}.jar";

                string downloadUrl =
                    $"{_baseUrl}/versions/{minecraftVersion}/builds/{buildNum}/downloads/{fileName}";

                builds.Add(new ServerBuild
                {
                    Platform = Platform,
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

        /// <inheritdoc />
        public async Task<ServerBuild?> GetLatestBuildAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            var builds = await GetBuildsAsync(minecraftVersion, ct);
            return builds.Count > 0 ? builds[0] : null;
        }

        /// <inheritdoc />
        public async Task<DownloadTask> DownloadAsync(
            ServerBuild build, string destinationPath,
            DownloadManager? downloadManager = null, CancellationToken ct = default)
        {
            var mgr = downloadManager ?? DownloadManager.Default;

            var task = new DownloadTask
            {
                DisplayName = $"{Platform} {build.MinecraftVersion} #{build.BuildNumber}",
                Url = build.DownloadUrl,
                DestinationPath = destinationPath,
                ExpectedHash = build.Sha256,
                HashAlgorithm = "SHA256"
            };

            if (ct != default)
                ct.Register(() => task.Cts.Cancel());

            return await mgr.EnqueueAsync(task);
        }
    }
}
