using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 通用 PaperMC API v2 提供者。
    /// Paper / Folia / Velocity 共用同一套 API，仅 project 名不同。
    /// </summary>
    public class PaperProvider : IServerProvider
    {
        private readonly string _project;
        private readonly string _baseUrl;
        private readonly HttpClient _http;

        /// <summary>
        /// 平台标识（由构造函数传入的 project 决定）。
        /// </summary>
        public ServerPlatform Platform { get; }

        /// <summary>
        /// 创建一个 PaperMC API v2 提供者。
        /// </summary>
        /// <param name="platform">平台枚举</param>
        /// <param name="project">API 项目名 (paper / folia / velocity)</param>
        /// <param name="httpClient">可选共享 HttpClient</param>
        public PaperProvider(
            ServerPlatform platform = ServerPlatform.Paper,
            string project = "paper",
            HttpClient? httpClient = null)
        {
            Platform = platform;
            _project = project;
            _baseUrl = $"https://api.papermc.io/v2/projects/{_project}";
            _http = httpClient ?? CreateDefaultClient();
        }

        // ────────── 快捷静态工厂 ──────────

        public static PaperProvider CreatePaper(HttpClient? http = null)
            => new(ServerPlatform.Paper, "paper", http);

        public static PaperProvider CreateFolia(HttpClient? http = null)
            => new(ServerPlatform.Folia, "folia", http);

        public static PaperProvider CreateVelocity(HttpClient? http = null)
            => new(ServerPlatform.Velocity, "velocity", http);

        // ────────── 获取版本列表 ──────────

        public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
        {
            string json = await _http.GetStringAsync(_baseUrl, ct);
            using var doc = JsonDocument.Parse(json);

            var versions = doc.RootElement
                .GetProperty("versions")
                .EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();

            versions.Reverse();
            return versions.AsReadOnly();
        }

        // ────────── 获取构建列表 ──────────

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
                    ? ch.GetString() ?? "default"
                    : "default";

                string fileName = "";
                string? sha256 = null;

                if (buildElem.TryGetProperty("downloads", out var downloads)
                    && downloads.TryGetProperty("application", out var app))
                {
                    fileName = app.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    sha256 = app.TryGetProperty("sha256", out var s) ? s.GetString() : null;
                }

                if (string.IsNullOrEmpty(fileName))
                    fileName = $"{_project}-{minecraftVersion}-{buildNum}.jar";

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

        // ────────── 获取最新构建 ──────────

        public async Task<ServerBuild?> GetLatestBuildAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            var builds = await GetBuildsAsync(minecraftVersion, ct);
            return builds.FirstOrDefault();
        }

        // ────────── 下载 ──────────

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

        private static HttpClient CreateDefaultClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SimplyMinecraftServerManager/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }
    }
}