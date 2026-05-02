using System.Net.Http;
using System.Text.Json;

namespace SimplyMinecraftServerManager.Internals.Downloads.JDK
{
    /// <summary>
    /// Eclipse Adoptium (Temurin) JDK 提供者。
    /// https://api.adoptium.net/v3
    /// </summary>
    public class AdoptiumProvider(HttpClient? httpClient = null) : IJdkProvider
    {
        private const string BaseUrl = "https://api.adoptium.net/v3";
        private readonly HttpClient _http = httpClient ?? CreateDefaultClient();

        public JdkDistribution Distribution => JdkDistribution.Adoptium;

        // ────────── 可用版本 ──────────

        public async Task<IReadOnlyList<int>> GetAvailableMajorVersionsAsync(
            CancellationToken ct = default)
        {
            // GET /v3/info/available_releases
            // { "available_lts_releases": [8,11,17,21], "available_releases": [8,11,...,23], ... }
            string json = await _http.GetStringAsync(
                $"{BaseUrl}/info/available_releases", ct);

            using var doc = JsonDocument.Parse(json);

            var versions = doc.RootElement
                .GetProperty("available_releases")
                .EnumerateArray()
                .Select(e => e.GetInt32())
                .OrderByDescending(v => v)
                .ToList();

            return versions.AsReadOnly();
        }

        // ────────── 构建列表 ──────────

        public async Task<IReadOnlyList<JdkInfo>> GetBuildsAsync(
            int majorVersion,
            JdkArchitecture? architecture = null,
            CancellationToken ct = default)
        {
            var arch = architecture ?? JdkArchitectureHelper.Current;
            string archStr = arch == JdkArchitecture.AArch64 ? "aarch64" : "x64";

            // GET /v3/assets/feature_releases/{version}/ga
            //   ?os=windows&architecture=x64&image_type=jdk&jvm_impl=hotspot&page_size=20
            string url = $"{BaseUrl}/assets/feature_releases/{majorVersion}/ga"
                + $"?os=windows"
                + $"&architecture={archStr}"
                + $"&image_type=jdk"
                + $"&jvm_impl=hotspot"
                + $"&page_size=20"
                + $"&sort_order=DESC";

            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var results = new List<JdkInfo>();

            foreach (var asset in doc.RootElement.EnumerateArray())
            {
                // 每个 asset 内可能有多个 binary
                if (!asset.TryGetProperty("binaries", out var binaries))
                    continue;

                string fullVersion = "";
                if (asset.TryGetProperty("version_data", out var vd))
                {
                    fullVersion = vd.TryGetProperty("openjdk_version", out var ov)
                        ? ov.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(fullVersion) &&
                        vd.TryGetProperty("semver", out var sv))
                        fullVersion = sv.GetString() ?? "";
                }

                foreach (var bin in binaries.EnumerateArray())
                {
                    if (!bin.TryGetProperty("package", out var pkg))
                        continue;

                    string pkgLink = pkg.TryGetProperty("link", out var l)
                        ? l.GetString() ?? "" : "";
                    string pkgName = pkg.TryGetProperty("name", out var n)
                        ? n.GetString() ?? "" : "";
                    long pkgSize = pkg.TryGetProperty("size", out var s)
                        ? s.GetInt64() : -1;
                    string? pkgChecksum = pkg.TryGetProperty("checksum", out var c)
                        ? c.GetString() : null;

                    // 仅保留 zip 包（Windows）
                    string ext = pkgName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        ? "zip"
                        : pkgName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                            ? "tar.gz"
                            : pkgName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                                ? "msi"
                                : "";

                    if (ext != "zip" && ext != "tar.gz") continue;

                    results.Add(new JdkInfo
                    {
                        Distribution = JdkDistribution.Adoptium,
                        MajorVersion = majorVersion,
                        FullVersion = fullVersion,
                        Architecture = arch,
                        DownloadUrl = pkgLink,
                        FileName = pkgName,
                        FileSize = pkgSize,
                        Sha256 = pkgChecksum,
                        PackageType = ext
                    });
                }
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