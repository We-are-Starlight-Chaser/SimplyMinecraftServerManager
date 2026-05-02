using System.Net.Http;
using System.Text.Json;

namespace SimplyMinecraftServerManager.Internals.Downloads.JDK
{
    /// <summary>
    /// Azul Zulu JDK 提供者。
    /// https://api.azul.com/metadata/v1/zulu/packages/
    /// </summary>
    public class ZuluProvider(HttpClient? httpClient = null) : IJdkProvider
    {
        private const string BaseUrl = "https://api.azul.com/metadata/v1/zulu/packages/";
        private readonly HttpClient _http = httpClient ?? CreateDefaultClient();

        public JdkDistribution Distribution => JdkDistribution.Zulu;

        // ────────── 可用版本 ──────────

        public async Task<IReadOnlyList<int>> GetAvailableMajorVersionsAsync(
            CancellationToken ct = default)
        {
            // 查询所有 Windows JDK 包，提取不重复的主版本号
            string url = BaseUrl
                + "?os=windows"
                + "&arch=x64"
                + "&java_package_type=jdk"
                + "&archive_type=zip"
                + "&javafx_bundled=false"
                + "&release_status=ga"
                + "&availability_types=CA"
                + "&page=1"
                + "&page_size=100";

            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var versions = new HashSet<int>();

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                if (elem.TryGetProperty("java_version", out var jv))
                {
                    var arr = jv.EnumerateArray().ToList();
                    if (arr.Count > 0)
                        versions.Add(arr[0].GetInt32());
                }
            }

            return versions.OrderByDescending(v => v).ToList().AsReadOnly();
        }

        // ────────── 构建列表 ──────────

        public async Task<IReadOnlyList<JdkInfo>> GetBuildsAsync(
            int majorVersion,
            JdkArchitecture? architecture = null,
            CancellationToken ct = default)
        {
            var arch = architecture ?? JdkArchitectureHelper.Current;
            string archStr = arch == JdkArchitecture.AArch64 ? "aarch64" : "x64";

            string url = BaseUrl
                + $"?os=windows"
                + $"&arch={archStr}"
                + $"&java_package_type=jdk"
                + $"&archive_type=zip"
                + $"&javafx_bundled=false"
                + $"&release_status=ga"
                + $"&availability_types=CA"
                + $"&java_version={majorVersion}"
                + $"&page=1"
                + $"&page_size=20";

            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var results = new List<JdkInfo>();

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                string downloadUrl = elem.TryGetProperty("download_url", out var du)
                    ? du.GetString() ?? "" : "";
                string name = elem.TryGetProperty("name", out var n)
                    ? n.GetString() ?? "" : "";
                string? sha256 = elem.TryGetProperty("sha256_hash", out var sh)
                    ? sh.GetString() : null;

                // 拼完整版本
                string fullVersion = "";
                if (elem.TryGetProperty("java_version", out var jv))
                {
                    var parts = jv.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                    fullVersion = string.Join(".", parts);
                }

                if (string.IsNullOrEmpty(downloadUrl)) continue;

                results.Add(new JdkInfo
                {
                    Distribution = JdkDistribution.Zulu,
                    MajorVersion = majorVersion,
                    FullVersion = fullVersion,
                    Architecture = arch,
                    DownloadUrl = downloadUrl,
                    FileName = name,
                    FileSize = -1,
                    Sha256 = sha256,
                    PackageType = "zip"
                });
            }

            return results.AsReadOnly();
        }

        // ────────── 最新构建 ──────────

        public async Task<JdkInfo?> GetLatestAsync(
            int majorVersion,
            JdkArchitecture? architecture = null,
            CancellationToken ct = default)
        {
            var builds = await GetBuildsAsync(majorVersion, architecture, ct);
            return builds[0];
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