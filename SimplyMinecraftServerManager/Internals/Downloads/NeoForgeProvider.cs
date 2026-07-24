// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using SimplyMinecraftServerManager.Helpers;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// NeoForge Maven 提供者 (maven.neoforged.net)。
    /// 通过 Maven 元数据 XML 获取版本列表，下载 installer JAR 启动服务器。
    /// https://neoforged.net/
    /// </summary>
    public class NeoForgeProvider : IServerProvider
    {
        private const string MavenBaseUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
        private readonly HttpClient _http;
        private static readonly MemoryCache<IReadOnlyList<string>> _versionsCache = new(TimeSpan.FromMinutes(10), 10);

        public ServerPlatform Platform => ServerPlatform.NeoForge;

        public NeoForgeProvider(HttpClient? httpClient = null)
        {
            _http = httpClient ?? HttpHelper.CreateDefaultClient();
        }

        /// <summary>
        /// 获取所有支持的 Minecraft 版本列表（降序）。
        /// 解析 Maven maven-metadata.xml 中的 version 列表，筛选出 MC 版本号。
        /// NeoForge 版本格式: {mcVersion}-{neoforgeVersion}，如 1.21.4-21.4.1
        /// </summary>
        public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
        {
            string cacheKey = "neoforge:versions";
            if (_versionsCache.TryGet(cacheKey, out var cached))
                return cached;

            string xml = await _http.GetStringAsync($"{MavenBaseUrl}/maven-metadata.xml", ct);
            var doc = XDocument.Parse(xml);

            // maven-metadata.xml 结构: <metadata><versions><version>1.21.4-21.4.1</version>...</versions></metadata>
            var versions = doc.Descendants("version")
                .Select(v => v.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(ParseMinecraftVersion)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .OrderByDescending(v => ParseComparableVersion(v))
                .ToList();

            var result = versions.AsReadOnly();
            _versionsCache.Set(cacheKey, result);
            return result;
        }

        /// <summary>
        /// 获取指定 MC 版本的所有构建（该 MC 版本对应的所有 NeoForge 版本）。
        /// </summary>
        public async Task<IReadOnlyList<ServerBuild>> GetBuildsAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            string xml = await _http.GetStringAsync($"{MavenBaseUrl}/maven-metadata.xml", ct);
            var doc = XDocument.Parse(xml);

            var builds = new List<ServerBuild>();
            int buildIndex = 0;

            foreach (var versionElem in doc.Descendants("version"))
            {
                string fullVersion = versionElem.Value;
                string mcVer = ParseMinecraftVersion(fullVersion);

                if (!string.Equals(mcVer, minecraftVersion, StringComparison.OrdinalIgnoreCase))
                    continue;

                string fileName = $"neoforge-{fullVersion}-installer.jar";
                string downloadUrl = $"{MavenBaseUrl}/{fullVersion}/{fileName}";

                builds.Add(new ServerBuild
                {
                    Platform = ServerPlatform.NeoForge,
                    MinecraftVersion = minecraftVersion,
                    BuildNumber = buildIndex++,
                    Channel = "stable",
                    FileName = fileName,
                    DownloadUrl = downloadUrl,
                    Sha256 = null,
                });
            }

            builds.Reverse();
            return builds.AsReadOnly();
        }

        /// <summary>
        /// 获取指定 MC 版本的最新 NeoForge 构建。
        /// </summary>
        public async Task<ServerBuild?> GetLatestBuildAsync(
            string minecraftVersion, CancellationToken ct = default)
        {
            var builds = await GetBuildsAsync(minecraftVersion, ct);
            return builds.Count > 0 ? builds[0] : null;
        }

        /// <summary>
        /// 下载 NeoForge installer JAR 到目标路径。
        /// </summary>
        public async Task<DownloadTask> DownloadAsync(
            ServerBuild build, string destinationPath,
            DownloadManager? downloadManager = null, CancellationToken ct = default)
        {
            var mgr = downloadManager ?? DownloadManager.Default;

            var task = new DownloadTask
            {
                DisplayName = $"NeoForge {build.MinecraftVersion} (build #{build.BuildNumber})",
                Url = build.DownloadUrl,
                DestinationPath = destinationPath,
                ExpectedHash = build.Sha256,
                HashAlgorithm = "SHA256",
            };

            if (ct != default)
                ct.Register(() => task.Cts.Cancel());

            return await mgr.EnqueueAsync(task);
        }

        /// <summary>
        /// 下载完成后运行 NeoForge installer，生成实际的服务器 JAR。
        /// 执行: java -jar neoforge-{version}-installer.jar --install-server
        /// </summary>
        public async Task<string> AfterDownloadAsync(
            string downloadedFilePath,
            string workingDirectory,
            CancellationToken ct = default)
        {
            string installerFileName = Path.GetFileName(downloadedFilePath);
            string javaPath = ResolveJavaPath();

            // 运行 installer: java -jar {installer} --install-server
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = $"-jar \"{installerFileName}\" --install-server",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 NeoForge installer");

            // 读取输出避免进程阻塞
            string output = await process.StandardOutput.ReadToEndAsync(ct);
            string errors = await process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"NeoForge installer 失败 (退出码 {process.ExitCode}): {errors}");
            }

            // installer 完成后在工作目录查找生成的 universal jar
            string? universalJar = Directory.GetFiles(workingDirectory, "neoforge-*-universal.jar")
                .Concat(Directory.GetFiles(workingDirectory, "neoforge-*-server.jar"))
                .Concat(Directory.GetFiles(workingDirectory, "forge-*-server.jar"))
                .FirstOrDefault();

            // 有些版本生成的是 server.jar
            universalJar ??= Directory.GetFiles(workingDirectory, "server.jar")
                .FirstOrDefault(f => f != downloadedFilePath);

            if (string.IsNullOrEmpty(universalJar))
            {
                throw new InvalidOperationException(
                    $"NeoForge installer 完成但未找到生成的服务器 JAR。安装输出: {output}");
            }

            return universalJar;
        }

        /// <summary>
        /// 查找系统中可用的 Java 路径。
        /// </summary>
        private static string ResolveJavaPath()
        {
            // 优先使用 PATH 中的 java
            string? javaInPath = Environment.GetEnvironmentVariable("PATH")
                ?.Split(Path.PathSeparator)
                .Select(p => Path.Combine(p, "java.exe"))
                .FirstOrDefault(File.Exists);

            if (javaInPath != null)
                return javaInPath;

            throw new InvalidOperationException(
                "未找到 Java 运行时，请先安装 JDK 或在设置中配置 Java 路径。");
        }

        /// <summary>
        /// 从 NeoForge 完整版本号中提取 MC 版本。
        /// 输入: "1.21.4-21.4.1" → 输出: "1.21.4"
        /// </summary>
        private static string ParseMinecraftVersion(string fullVersion)
        {
            int separatorIndex = fullVersion.IndexOf('-');
            return separatorIndex > 0 ? fullVersion[..separatorIndex] : fullVersion;
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
