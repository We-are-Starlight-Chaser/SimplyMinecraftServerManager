using System.Net.Http;
using System.Text.Json;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// Leaves 服务端提供者。
    /// API 兼容 PaperMC v2 格式。
    /// https://api.leavesmc.org/v2/projects/leaves
    /// </summary>
    public class LeavesProvider(HttpClient? httpClient = null) : IServerProvider
    {
        private const string BaseUrl = "https://api.leavesmc.org/v2/projects/leaves";
        private readonly HttpClient _http = httpClient ?? CreateDefaultClient();

        public ServerPlatform Platform => ServerPlatform.Leaves;

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
                    fileName = $"leaves-{minecraftVersion}-{buildNum}.jar";

                string downloadUrl =
                    $"{BaseUrl}/versions/{minecraftVersion}/builds/{buildNum}/downloads/{fileName}";

                builds.Add(new ServerBuild
                {
                    Platform = ServerPlatform.Leaves,
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

        public async Task<ServerBuild?> GetLatestBuildAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            var builds = await GetBuildsAsync(minecraftVersion, ct);
            return builds.FirstOrDefault();
        }

        public async Task<DownloadTask> DownloadAsync(
            ServerBuild build, string destinationPath,
            DownloadManager? downloadManager = null, CancellationToken ct = default)
        {
            var mgr = downloadManager ?? DownloadManager.Default;

            var task = new DownloadTask
            {
                DisplayName = $"Leaves {build.MinecraftVersion} #{build.BuildNumber}",
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