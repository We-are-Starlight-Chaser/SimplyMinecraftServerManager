using System.Net.Http;
using System.Text.Json;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// Modrinth API v2 客户端。
    /// 支持搜索插件、获取项目详情、版本列表、下载。
    /// https://docs.modrinth.com/api/
    /// </summary>
    public class ModrinthProvider
    {
        private const string BaseUrl = "https://api.modrinth.com/v2";
        private readonly HttpClient _http;

        // JSON 选项：Modrinth 返回 snake_case
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        public ModrinthProvider(HttpClient? httpClient = null)
        {
            _http = httpClient ?? CreateDefaultClient();
        }

        // 搜索

        /// <summary>
        /// 搜索 Modrinth 插件/模组。
        /// </summary>
        /// <param name="query">搜索关键词</param>
        /// <param name="loaders">加载器过滤 (bukkit/spigot/paper/purpur/folia…)，可多选</param>
        /// <param name="gameVersions">MC 版本过滤，可多选</param>
        /// <param name="projectType">项目类型过滤 (plugin / mod / datapack)，传 null 不过滤</param>
        /// <param name="offset">分页偏移</param>
        /// <param name="limit">每页数量 (1-100)</param>
        /// <param name="sortBy">排序方式: relevance / downloads / follows / newest / updated</param>
        /// <param name="ct">取消令牌</param>
        public async Task<ModrinthSearchResponse> SearchAsync(
            string query,
            IEnumerable<string>? loaders = null,
            IEnumerable<string>? gameVersions = null,
            string? projectType = "plugin",
            int offset = 0,
            int limit = 20,
            string sortBy = "relevance",
            CancellationToken ct = default)
        {
            // 构建 facets
            // Modrinth facets 格式: [["categories:bukkit","categories:paper"],["versions:1.21.4"],["project_type:plugin"]]
            var facetGroups = new List<string>();

            if (loaders != null)
            {
                var loaderList = loaders.ToList();
                if (loaderList.Count > 0)
                {
                    string group = string.Join(",",
                        loaderList.Select(l => $"\"categories:{l}\""));
                    facetGroups.Add($"[{group}]");
                }
            }

            if (gameVersions != null)
            {
                var versionList = gameVersions.ToList();
                if (versionList.Count > 0)
                {
                    string group = string.Join(",",
                        versionList.Select(v => $"\"versions:{v}\""));
                    facetGroups.Add($"[{group}]");
                }
            }

            if (!string.IsNullOrEmpty(projectType))
            {
                facetGroups.Add($"[\"project_type:{projectType}\"]");
            }

            // 只搜索服务端兼容的项目
            facetGroups.Add("[\"server_side:required\",\"server_side:optional\"]");

            string facets = facetGroups.Count > 0
                ? $"[{string.Join(",", facetGroups)}]"
                : "";

            string url = $"{BaseUrl}/search"
                + $"?query={Uri.EscapeDataString(query)}"
                + $"&offset={offset}"
                + $"&limit={Math.Clamp(limit, 1, 100)}"
                + $"&index={sortBy}";

            if (!string.IsNullOrEmpty(facets))
                url += $"&facets={Uri.EscapeDataString(facets)}";

            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var response = new ModrinthSearchResponse
            {
                Offset = root.GetProperty("offset").GetInt32(),
                Limit = root.GetProperty("limit").GetInt32(),
                TotalHits = root.GetProperty("total_hits").GetInt32()
            };

            foreach (var hit in root.GetProperty("hits").EnumerateArray())
            {
                response.Hits.Add(ParseProjectFromSearch(hit));
            }

            return response;
        }

        // 项目详情

        /// <summary>
        /// 获取项目详情。
        /// </summary>
        /// <param name="idOrSlug">项目 ID 或 slug</param>
        public async Task<ModrinthProject> GetProjectAsync(
            string idOrSlug, CancellationToken ct = default)
        {
            string url = $"{BaseUrl}/project/{Uri.EscapeDataString(idOrSlug)}";
            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            return ParseProjectDetail(doc.RootElement);
        }

        // 版本列表

        /// <summary>
        /// 获取项目的所有版本。
        /// </summary>
        /// <param name="idOrSlug">项目 ID 或 slug</param>
        /// <param name="loaders">可选加载器过滤</param>
        /// <param name="gameVersions">可选 MC 版本过滤</param>
        public async Task<List<ModrinthVersion>> GetVersionsAsync(
            string idOrSlug,
            IEnumerable<string>? loaders = null,
            IEnumerable<string>? gameVersions = null,
            CancellationToken ct = default)
        {
            string url = $"{BaseUrl}/project/{Uri.EscapeDataString(idOrSlug)}/version";

            var queryParts = new List<string>();

            if (loaders != null)
            {
                var list = loaders.ToList();
                if (list.Count > 0)
                    queryParts.Add($"loaders=[{string.Join(",", list.Select(l => $"\"{l}\""))}]");
            }

            if (gameVersions != null)
            {
                var list = gameVersions.ToList();
                if (list.Count > 0)
                    queryParts.Add($"game_versions=[{string.Join(",", list.Select(v => $"\"{v}\""))}]");
            }

            if (queryParts.Count > 0)
                url += "?" + string.Join("&", queryParts);

            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            var versions = new List<ModrinthVersion>();

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                versions.Add(ParseVersion(elem));
            }

            return versions;
        }

        /// <summary>
        /// 获取单个版本详情。
        /// </summary>
        public async Task<ModrinthVersion> GetVersionAsync(
            string versionId, CancellationToken ct = default)
        {
            string url = $"{BaseUrl}/version/{versionId}";
            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            return ParseVersion(doc.RootElement);
        }

        // 下载

        /// <summary>
        /// 下载指定版本的主文件到目标路径。
        /// </summary>
        public async Task<DownloadTask> DownloadVersionAsync(
            ModrinthVersion version,
            string destinationPath,
            DownloadManager? downloadManager = null,
            CancellationToken ct = default)
        {
            var file = version.PrimaryFile
                ?? throw new InvalidOperationException("Version has no files.");

            return await DownloadFileAsync(
                file, destinationPath, version.Name, downloadManager, ct);
        }

        /// <summary>
        /// 下载指定文件。
        /// </summary>
        public async Task<DownloadTask> DownloadFileAsync(
            ModrinthFile file,
            string destinationPath,
            string? displayName = null,
            DownloadManager? downloadManager = null,
            CancellationToken ct = default)
        {
            var mgr = downloadManager ?? DownloadManager.Default;

            // 优先使用 SHA-1 校验
            string? hash = null;
            string hashAlgo = "SHA1";

            if (file.Hashes.TryGetValue("sha1", out var sha1))
            {
                hash = sha1;
                hashAlgo = "SHA1";
            }
            else if (file.Hashes.TryGetValue("sha512", out var sha512))
            {
                hash = sha512;
                hashAlgo = "SHA512";
            }

            var task = new DownloadTask
            {
                DisplayName = displayName ?? file.FileName,
                Url = file.Url,
                DestinationPath = destinationPath,
                ExpectedHash = hash,
                HashAlgorithm = hashAlgo
            };

            if (ct != default)
                ct.Register(() => task.Cts.Cancel());

            return await mgr.EnqueueAsync(task);
        }

        /// <summary>
        /// 便捷方法：搜索 → 获取第一个结果的最新版本 → 下载到实例 plugins 目录。
        /// </summary>
        public async Task<DownloadTask?> SearchAndDownloadAsync(
            string query,
            string instanceId,
            string? mcVersion = null,
            IEnumerable<string>? loaders = null,
            DownloadManager? downloadManager = null,
            CancellationToken ct = default)
        {
            // 搜索
            var searchResult = await SearchAsync(
                query,
                loaders: loaders ?? new[] { "bukkit", "spigot", "paper", "purpur" },
                gameVersions: mcVersion != null ? new[] { mcVersion } : null,
                projectType: "plugin",
                limit: 1,
                ct: ct);

            if (searchResult.Hits.Count == 0)
                return null;

            var project = searchResult.Hits[0];

            // 获取版本
            var versions = await GetVersionsAsync(
                project.ProjectId,
                loaders: loaders ?? new[] { "bukkit", "spigot", "paper", "purpur" },
                gameVersions: mcVersion != null ? new[] { mcVersion } : null,
                ct: ct);

            if (versions.Count == 0)
                return null;

            var latestVersion = versions[0]; // API 默认按时间降序
            var primaryFile = latestVersion.PrimaryFile;
            if (primaryFile == null)
                return null;

            // 下载到 plugins 目录
            string destPath = System.IO.Path.Combine(
                PathHelper.GetPluginsDir(instanceId),
                primaryFile.FileName);

            return await DownloadFileAsync(
                primaryFile, destPath,
                $"{project.Title} v{latestVersion.VersionNumber}",
                downloadManager, ct);
        }

        // JSON 解析

        private static ModrinthProject ParseProjectFromSearch(JsonElement elem)
        {
            var project = new ModrinthProject
            {
                ProjectId = GetStr(elem, "project_id"),
                Slug = GetStr(elem, "slug"),
                Title = GetStr(elem, "title"),
                Description = GetStr(elem, "description"),
                Author = GetStr(elem, "author"),
                IconUrl = GetStr(elem, "icon_url"),
                ProjectType = GetStr(elem, "project_type"),
                Downloads = elem.TryGetProperty("downloads", out var d) ? d.GetInt64() : 0,
                Follows = elem.TryGetProperty("follows", out var f) ? f.GetInt32() : 0,
                ServerSide = GetStr(elem, "server_side"),
                ClientSide = GetStr(elem, "client_side"),
                LatestVersionId = GetStr(elem, "latest_version"),
                LatestGameVersion = GetStr(elem, "latest_version"),
            };

            if (elem.TryGetProperty("versions", out var versions))
            {
                project.GameVersions = versions.EnumerateArray()
                    .Select(v => v.GetString()!)
                    .ToList();
            }

            if (elem.TryGetProperty("categories", out var cats))
            {
                project.Loaders = cats.EnumerateArray()
                    .Select(c => c.GetString()!)
                    .ToList();
            }

            // display_categories 中也可能有加载器信息
            if (elem.TryGetProperty("display_categories", out var dcats))
            {
                var displayCats = dcats.EnumerateArray()
                    .Select(c => c.GetString()!)
                    .ToList();
                foreach (var c in displayCats)
                {
                    if (!project.Loaders.Contains(c))
                        project.Loaders.Add(c);
                }
            }

            return project;
        }

        private static ModrinthProject ParseProjectDetail(JsonElement elem)
        {
            var project = new ModrinthProject
            {
                ProjectId = GetStr(elem, "id"),
                Slug = GetStr(elem, "slug"),
                Title = GetStr(elem, "title"),
                Description = GetStr(elem, "description"),
                IconUrl = GetStr(elem, "icon_url"),
                ProjectType = GetStr(elem, "project_type"),
                Downloads = elem.TryGetProperty("downloads", out var d) ? d.GetInt64() : 0,
                Follows = elem.TryGetProperty("followers", out var f) ? f.GetInt32() : 0,
                ServerSide = GetStr(elem, "server_side"),
                ClientSide = GetStr(elem, "client_side"),
            };

            if (elem.TryGetProperty("game_versions", out var gv))
            {
                project.GameVersions = gv.EnumerateArray()
                    .Select(v => v.GetString()!).ToList();
            }

            if (elem.TryGetProperty("loaders", out var loaders))
            {
                project.Loaders = loaders.EnumerateArray()
                    .Select(l => l.GetString()!).ToList();
            }

            return project;
        }

        private static ModrinthVersion ParseVersion(JsonElement elem)
        {
            var version = new ModrinthVersion
            {
                Id = GetStr(elem, "id"),
                ProjectId = GetStr(elem, "project_id"),
                Name = GetStr(elem, "name"),
                VersionNumber = GetStr(elem, "version_number"),
                Changelog = GetStr(elem, "changelog"),
                VersionType = GetStr(elem, "version_type"),
                Downloads = elem.TryGetProperty("downloads", out var d) ? d.GetInt32() : 0,
                DatePublished = GetStr(elem, "date_published"),
            };

            if (elem.TryGetProperty("game_versions", out var gv))
            {
                version.GameVersions = gv.EnumerateArray()
                    .Select(v => v.GetString()!).ToList();
            }

            if (elem.TryGetProperty("loaders", out var loaders))
            {
                version.Loaders = loaders.EnumerateArray()
                    .Select(l => l.GetString()!).ToList();
            }

            if (elem.TryGetProperty("files", out var files))
            {
                foreach (var fileElem in files.EnumerateArray())
                {
                    var file = new ModrinthFile
                    {
                        Url = GetStr(fileElem, "url"),
                        FileName = GetStr(fileElem, "filename"),
                        Size = fileElem.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                        Primary = fileElem.TryGetProperty("primary", out var p) && p.GetBoolean(),
                    };

                    if (fileElem.TryGetProperty("hashes", out var hashes))
                    {
                        foreach (var hashProp in hashes.EnumerateObject())
                        {
                            file.Hashes[hashProp.Name] = hashProp.Value.GetString() ?? "";
                        }
                    }

                    version.Files.Add(file);
                }
            }

            return version;
        }

        private static string GetStr(JsonElement elem, string prop)
        {
            return elem.TryGetProperty(prop, out var val)
                ? val.ValueKind == JsonValueKind.Null ? "" : val.GetString() ?? ""
                : "";
        }

        private static HttpClient CreateDefaultClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SimplyMinecraftServerManager/1.0 (smsm@example.com)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }
    }
}
