// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Net.Http;
using System.Text.Json;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 通用 PaperMC API v3 提供者（fill.papermc.io）。
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
        /// 创建一个 PaperMC API v3 提供者。
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
            _baseUrl = $"https://fill.papermc.io/v3/projects/{_project}";
            _http = httpClient ?? CreateDefaultClient();
        }

        // ────────── 快捷静态工厂 ──────────

        /// <summary>
        /// 创建 Paper 平台提供者。
        /// </summary>
        /// <param name="http">可选共享 HttpClient</param>
        /// <returns>Paper 提供者实例</returns>
        public static PaperProvider CreatePaper(HttpClient? http = null)
            => new(ServerPlatform.Paper, "paper", http);

        /// <summary>
        /// 创建 Folia 平台提供者。
        /// </summary>
        /// <param name="http">可选共享 HttpClient</param>
        /// <returns>Folia 提供者实例</returns>
        public static PaperProvider CreateFolia(HttpClient? http = null)
            => new(ServerPlatform.Folia, "folia", http);

        /// <summary>
        /// 创建 Velocity 代理端提供者。
        /// </summary>
        /// <param name="http">可选共享 HttpClient</param>
        /// <returns>Velocity 提供者实例</returns>
        public static PaperProvider CreateVelocity(HttpClient? http = null)
            => new(ServerPlatform.Velocity, "velocity", http);

        // ────────── 获取版本列表 ──────────

        /// <summary>
        /// 获取所有支持的 Minecraft 版本列表（降序排列）。
        /// fill.papermc.io 返回 { "versions": { "1.21": [...], "1.20": [...] } } 嵌套结构。
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>版本号只读列表</returns>
        public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
        {
            using var response = await _http.GetAsync(_baseUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Paper API returned {(int)response.StatusCode}: {body}");
            }
            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var versions = new List<string>();
            var versionsElement = doc.RootElement.GetProperty("versions");

            foreach (var group in versionsElement.EnumerateObject())
            {
                if (group.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in group.Value.EnumerateArray())
                    {
                        versions.Add(v.GetString()!);
                    }
                }
            }

            return versions.AsReadOnly();
        }

        // ────────── 获取构建列表 ──────────

        /// <summary>
        /// 获取指定 Minecraft 版本的所有构建（降序排列，最新在前）。
        /// fill.papermc.io 返回扁平数组 [ {"id":132, "downloads":{"server:default":{...}}} ]。
        /// </summary>
        /// <param name="minecraftVersion">Minecraft 版本号</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>构建列表只读集合</returns>
        public async Task<IReadOnlyList<ServerBuild>> GetBuildsAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            string url = $"{_baseUrl}/versions/{minecraftVersion}/builds";
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Paper API returned {(int)response.StatusCode}: {body}");
            }
            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var builds = new List<ServerBuild>();

            var buildsArray = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("builds", out var b) ? b : default;

            foreach (var buildElem in buildsArray.EnumerateArray())
            {
                int buildNum = buildElem.GetProperty("id").GetInt32();
                string channel = buildElem.TryGetProperty("channel", out var ch)
                    ? ch.GetString() ?? "default"
                    : "default";

                string fileName = "";
                string? sha256 = null;
                string downloadUrl = "";

                if (buildElem.TryGetProperty("downloads", out var downloads))
                {
                    var downloadKey = downloads.EnumerateObject().FirstOrDefault();
                    if (downloadKey.Value.ValueKind == JsonValueKind.Object)
                    {
                        var downloadObj = downloadKey.Value;
                        fileName = downloadObj.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        downloadUrl = downloadObj.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";

                        if (downloadObj.TryGetProperty("checksums", out var checksums)
                            && checksums.TryGetProperty("sha256", out var s))
                        {
                            sha256 = s.GetString();
                        }
                    }
                }

                if (string.IsNullOrEmpty(fileName))
                    fileName = $"{_project}-{minecraftVersion}-{buildNum}.jar";

                if (string.IsNullOrEmpty(downloadUrl))
                    downloadUrl = $"{_baseUrl}/versions/{minecraftVersion}/builds/{buildNum}/downloads/{fileName}";

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

        /// <summary>
        /// 获取指定 Minecraft 版本的最新构建。
        /// </summary>
        /// <param name="minecraftVersion">Minecraft 版本号</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>最新构建，无可用构建时返回 null</returns>
        public async Task<ServerBuild?> GetLatestBuildAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            var builds = await GetBuildsAsync(minecraftVersion, ct);
            return builds.Count > 0 ? builds[0] : null;
        }

        // ────────── 下载 ──────────

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

        /// <summary>
        /// 创建默认的 HttpClient 实例，超时时间 30 分钟。
        /// </summary>
        private static HttpClient CreateDefaultClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "SimplyMinecraftServerManager/1.0 (https://github.com/We-are-Starlight-Chaser/SimplyMinecraftServerManager)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            return client;
        }
    }
}
