using System.IO;
using System.IO.Compression;

namespace SimplyMinecraftServerManager.Internals.Downloads.JDK
{
    /// <summary>
    /// 已安装 JDK 的本地记录。
    /// </summary>
    public class InstalledJdk
    {
        /// <summary>发行版</summary>
        public JdkDistribution Distribution { get; init; }

        /// <summary>主版本号</summary>
        public int MajorVersion { get; init; }

        /// <summary>完整版本</summary>
        public string FullVersion { get; init; } = "";

        /// <summary>CPU 架构</summary>
        public JdkArchitecture Architecture { get; init; }

        /// <summary>JDK 根目录 (包含 bin/ 子目录)</summary>
        public string HomePath { get; init; } = "";

        /// <summary>java.exe 完整路径</summary>
        public string JavaExecutable => Path.Combine(HomePath, "bin", "java.exe");

        /// <summary>java.exe 是否存在</summary>
        public bool IsValid => File.Exists(JavaExecutable);

        public override string ToString()
            => $"{Distribution} JDK {FullVersion} ({Architecture}) → {HomePath}";
    }

    /// <summary>
    /// JDK 下载、解压、管理器。
    /// 所有 JDK 安装在 %appdata%/smsm/jdks/{distribution}-{major}-{arch}/
    /// </summary>
    public static class JdkManager
    {
        /// <summary>
        /// 获取所有已安装的 JDK。
        /// </summary>
        public static IReadOnlyList<InstalledJdk> GetInstalledJdks()
        {
            string jdksRoot = PathHelper.JdksRoot;
            if (!Directory.Exists(jdksRoot))
                return Array.Empty<InstalledJdk>();

            var results = new List<InstalledJdk>();

            foreach (string dir in Directory.EnumerateDirectories(jdksRoot))
            {
                string dirName = Path.GetFileName(dir);
                // 格式: adoptium-21-x64  /  zulu-17-aarch64
                string[] parts = dirName.Split('-');
                if (parts.Length < 3) continue;

                if (!Enum.TryParse<JdkDistribution>(parts[0], ignoreCase: true, out var dist))
                    continue;

                if (!int.TryParse(parts[1], out int major))
                    continue;

                var arch = parts[2].Equals("aarch64", StringComparison.OrdinalIgnoreCase)
                    ? JdkArchitecture.AArch64
                    : JdkArchitecture.X64;

                // 找 JDK 根目录：可能是 dir 本身，也可能是 dir 下唯一的子目录
                string homePath = ResolveJdkHome(dir);

                // 读版本
                string fullVersion = TryReadVersion(homePath) ?? $"{major}";

                results.Add(new InstalledJdk
                {
                    Distribution = dist,
                    MajorVersion = major,
                    FullVersion = fullVersion,
                    Architecture = arch,
                    HomePath = homePath
                });
            }

            return results
                .OrderByDescending(j => j.MajorVersion)
                .ThenBy(j => j.Distribution.ToString())
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// 获取已安装的指定主版本 JDK，不存在返回 null。
        /// </summary>
        public static InstalledJdk? GetInstalled(
            int majorVersion,
            JdkDistribution? distribution = null,
            JdkArchitecture? architecture = null)
        {
            var arch = architecture ?? JdkArchitectureHelper.Current;

            return GetInstalledJdks().FirstOrDefault(j =>
                j.MajorVersion == majorVersion
                && j.Architecture == arch
                && (distribution == null || j.Distribution == distribution));
        }

        /// <summary>
        /// 获取指定主版本 JDK 的 java.exe 路径。
        /// 未安装返回 null。
        /// </summary>
        public static string? GetJavaExecutable(
            int majorVersion,
            JdkDistribution? distribution = null)
        {
            var jdk = GetInstalled(majorVersion, distribution);
            return jdk?.IsValid == true ? jdk.JavaExecutable : null;
        }

        /// <summary>
        /// 下载并安装 JDK。
        /// </summary>
        /// <param name="jdkInfo">要下载的 JDK 信息</param>
        /// <param name="downloadManager">下载管理器</param>
        /// <param name="progress">解压进度回调 (0~100)</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>安装完成的 JDK 信息</returns>
        public static async Task<InstalledJdk> DownloadAndInstallAsync(
            JdkInfo jdkInfo,
            DownloadManager? downloadManager = null,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            var mgr = downloadManager ?? DownloadManager.Default;

            PathHelper.EnsureDirectories();

            string archStr = jdkInfo.Architecture == JdkArchitecture.AArch64
                ? "aarch64" : "x64";

            string installDir = Path.Combine(
                PathHelper.JdksRoot,
                $"{jdkInfo.Distribution.ToString().ToLowerInvariant()}-{jdkInfo.MajorVersion}-{archStr}");

            // 如果已存在，先删除旧版
            if (Directory.Exists(installDir))
                Directory.Delete(installDir, recursive: true);

            Directory.CreateDirectory(installDir);

            // ───── 阶段 1：下载 ─────
            string tempArchive = Path.Combine(
                PathHelper.JdksRoot,
                $"_downloading_{jdkInfo.FileName}");

            var downloadTask = new DownloadTask
            {
                DisplayName = $"JDK {jdkInfo.Distribution} {jdkInfo.FullVersion}",
                Url = jdkInfo.DownloadUrl,
                DestinationPath = tempArchive,
                ExpectedHash = jdkInfo.Sha256,
                HashAlgorithm = "SHA256"
            };

            if (ct != default)
                ct.Register(() => downloadTask.Cts.Cancel());

            var result = await mgr.EnqueueAsync(downloadTask);

            if (result.Status != DownloadStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"JDK download failed: {result.ErrorMessage}");
            }

            // ───── 阶段 2：解压 ─────
            try
            {
                if (jdkInfo.PackageType == "zip")
                {
                    await ExtractZipAsync(tempArchive, installDir, progress, ct);
                }
                else if (jdkInfo.PackageType == "tar.gz")
                {
                    await ExtractTarGzAsync(tempArchive, installDir, progress, ct);
                }
                else
                {
                    throw new NotSupportedException(
                        $"Unsupported package type: {jdkInfo.PackageType}");
                }
            }
            finally
            {
                // 清理下载的压缩包
                try { if (File.Exists(tempArchive)) File.Delete(tempArchive); }
                catch { /* best effort */ }
            }

            // ───── 阶段 3：定位 JDK Home ─────
            string homePath = ResolveJdkHome(installDir);
            string fullVersion = TryReadVersion(homePath) ?? jdkInfo.FullVersion;

            var installed = new InstalledJdk
            {
                Distribution = jdkInfo.Distribution,
                MajorVersion = jdkInfo.MajorVersion,
                FullVersion = fullVersion,
                Architecture = jdkInfo.Architecture,
                HomePath = homePath
            };

            if (!installed.IsValid)
            {
                throw new FileNotFoundException(
                    $"java.exe not found after extraction: {installed.JavaExecutable}");
            }

            return installed;
        }

        /// <summary>
        /// 便捷方法：自动选择发行版，下载并安装指定主版本的最新 JDK。
        /// </summary>
        public static async Task<InstalledJdk> AutoInstallAsync(
            int majorVersion,
            JdkDistribution distribution = JdkDistribution.Adoptium,
            JdkArchitecture? architecture = null,
            DownloadManager? downloadManager = null,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            // 已安装则直接返回
            var existing = GetInstalled(majorVersion, distribution, architecture);
            if (existing?.IsValid == true)
                return existing;

            var provider = JdkProviderFactory.Get(distribution);
            var latest = await provider.GetLatestAsync(majorVersion, architecture, ct);

            if (latest == null)
                throw new InvalidOperationException(
                    $"No {distribution} JDK {majorVersion} build found.");

            return await DownloadAndInstallAsync(latest, downloadManager, progress, ct);
        }

        /// <summary>
        /// 确保指定主版本 JDK 已安装，返回 java.exe 路径。
        /// </summary>
        public static async Task<string> EnsureJdkAsync(
            int majorVersion,
            JdkDistribution distribution = JdkDistribution.Adoptium,
            DownloadManager? downloadManager = null,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            var jdk = await AutoInstallAsync(
                majorVersion, distribution, null, downloadManager, progress, ct);

            return jdk.JavaExecutable;
        }

        /// <summary>
        /// 删除已安装的 JDK。
        /// </summary>
        public static bool Uninstall(
            int majorVersion,
            JdkDistribution? distribution = null,
            JdkArchitecture? architecture = null)
        {
            var jdk = GetInstalled(majorVersion, distribution, architecture);
            if (jdk == null) return false;

            // 往上找到 installDir（jdksRoot 下的直接子目录）
            string jdksRoot = PathHelper.JdksRoot;
            string target = jdk.HomePath;

            // HomePath 可能是 installDir 或其子目录
            while (!string.IsNullOrEmpty(target)
                   && Path.GetDirectoryName(target) != jdksRoot)
            {
                target = Path.GetDirectoryName(target)!;
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 根据 Minecraft 版本推荐 JDK 主版本号。
        /// </summary>
        public static int RecommendJdkVersion(string minecraftVersion)
        {
            // 解析主版本 "1.21.4" → 21, "1.17" → 17, "1.16.5" → 16
            int mcMinor = 0;
            if (minecraftVersion.StartsWith("1."))
            {
                string after = minecraftVersion[2..];
                int dot = after.IndexOf('.');
                string minorStr = dot > 0 ? after[..dot] : after;
                int.TryParse(minorStr, out mcMinor);
            }

            return mcMinor switch
            {
                >= 21 => 21,   // 1.21+ → JDK 21
                >= 20 => 17,   // 1.20.5+ 实际需要 JDK 21，但 20.1~20.4 用 17
                >= 17 => 17,   // 1.17 ~ 1.20 → JDK 17
                >= 12 => 11,   // 1.12 ~ 1.16 → JDK 11 (或 8)
                _ => 8     // 1.7 ~ 1.11 → JDK 8
            };
        }

        // ════════════════════════════════════════════════
        //  内部工具
        // ════════════════════════════════════════════════

        /// <summary>
        /// 解压 ZIP 到目标目录（异步 + 进度回调）。
        /// </summary>
        private static async Task ExtractZipAsync(
            string zipPath, string destDir,
            IProgress<int>? progress, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(zipPath);
                int total = archive.Entries.Count;
                int done = 0;

                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    // 跳过目录条目
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        done++;
                        continue;
                    }

                    string destPath = Path.Combine(destDir, entry.FullName);

                    // 安全检查
                    string fullDest = Path.GetFullPath(destPath);
                    string fullDestDir = Path.GetFullPath(destDir);
                    if (!fullDest.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? parentDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parentDir))
                        Directory.CreateDirectory(parentDir);

                    entry.ExtractToFile(destPath, overwrite: true);

                    done++;
                    if (total > 0)
                        progress?.Report((int)((double)done / total * 100));
                }
            }, ct);
        }

        /// <summary>
        /// 解压 tar.gz 到目标目录。
        /// 使用 GZipStream + 手动 TAR 解析（纯 .NET，无外部依赖）。
        /// </summary>
        private static async Task ExtractTarGzAsync(
            string tarGzPath, string destDir,
            IProgress<int>? progress, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                using var fileStream = File.OpenRead(tarGzPath);
                using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);

                // .NET 7+ 有 TarReader
#if NET7_0_OR_GREATER
                using var tarReader = new System.Formats.Tar.TarReader(gzipStream);
                int entryIndex = 0;

                System.Formats.Tar.TarEntry? entry;
                while ((entry = tarReader.GetNextEntry()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    entryIndex++;

                    if (entry.EntryType is System.Formats.Tar.TarEntryType.Directory)
                        continue;

                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string destPath = Path.Combine(destDir, entry.Name);
                    string fullDest = Path.GetFullPath(destPath);
                    string fullDestDir = Path.GetFullPath(destDir);
                    if (!fullDest.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? parentDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parentDir))
                        Directory.CreateDirectory(parentDir);

                    entry.ExtractToFile(destPath, overwrite: true);

                    progress?.Report(entryIndex % 100); // 估计进度
                }
#else
                // .NET 6 或更早：用 System.IO.Compression 的 Tar 简单实现
                ExtractTarFromStream(gzipStream, destDir, ct);
#endif

                progress?.Report(100);
            }, ct);
        }

#if !NET7_0_OR_GREATER
        /// <summary>
        /// 简易 TAR 流解压器（用于 .NET 6 及更早版本）。
        /// </summary>
        private static void ExtractTarFromStream(
            Stream stream, string destDir, CancellationToken ct)
        {
            byte[] header = new byte[512];
            byte[] buffer = new byte[81920];

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // 读 512 字节头
                int bytesRead = ReadFull(stream, header, 0, 512);
                if (bytesRead < 512) break;

                // 全零头 = 结束
                if (header.All(b => b == 0)) break;

                // 文件名: 0~100
                string name = System.Text.Encoding.ASCII.GetString(header, 0, 100)
                    .TrimEnd('\0', ' ');

                // 文件大小: 124~136 (八进制 ASCII)
                string sizeStr = System.Text.Encoding.ASCII.GetString(header, 124, 12)
                    .TrimEnd('\0', ' ');

                long size = 0;
                if (!string.IsNullOrEmpty(sizeStr))
                    size = Convert.ToInt64(sizeStr, 8);

                // 类型: 156
                byte typeFlag = header[156];

                // 需要读取的 512 字节对齐块数
                long blocks = (size + 511) / 512;
                long totalDataBytes = blocks * 512;

                if (typeFlag == (byte)'5' || name.EndsWith('/'))
                {
                    // 目录
                    string dirPath = Path.Combine(destDir, name);
                    Directory.CreateDirectory(dirPath);
                    SkipBytes(stream, totalDataBytes);
                }
                else if (typeFlag == (byte)'0' || typeFlag == 0)
                {
                    // 普通文件
                    string filePath = Path.Combine(destDir, name);
                    string fullPath = Path.GetFullPath(filePath);
                    string fullDest = Path.GetFullPath(destDir);

                    if (!fullPath.StartsWith(fullDest, StringComparison.OrdinalIgnoreCase))
                    {
                        SkipBytes(stream, totalDataBytes);
                        continue;
                    }

                    string? parentDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(parentDir))
                        Directory.CreateDirectory(parentDir);

                    using (var fs = File.Create(filePath))
                    {
                        long remaining = size;
                        while (remaining > 0)
                        {
                            int toRead = (int)Math.Min(remaining, buffer.Length);
                            int read = ReadFull(stream, buffer, 0, toRead);
                            if (read <= 0) break;
                            fs.Write(buffer, 0, read);
                            remaining -= read;
                        }
                    }

                    // 跳过 padding
                    long padding = totalDataBytes - size;
                    if (padding > 0)
                        SkipBytes(stream, padding);
                }
                else
                {
                    // 其他类型，跳过
                    SkipBytes(stream, totalDataBytes);
                }
            }
        }

        private static int ReadFull(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
            return totalRead;
        }

        private static void SkipBytes(Stream stream, long count)
        {
            byte[] skip = new byte[Math.Min(count, 81920)];
            while (count > 0)
            {
                int toRead = (int)Math.Min(count, skip.Length);
                int read = stream.Read(skip, 0, toRead);
                if (read == 0) break;
                count -= read;
            }
        }
#endif

        /// <summary>
        /// ZIP/tar.gz 解压后找到 JDK Home 目录。
        /// 压缩包内通常有一层顶级目录如 jdk-21.0.4+7/。
        /// </summary>
        private static string ResolveJdkHome(string extractedDir)
        {
            // 如果 extractedDir/bin/java.exe 直接存在
            if (File.Exists(Path.Combine(extractedDir, "bin", "java.exe")))
                return extractedDir;

            // 查找子目录
            foreach (string subDir in Directory.EnumerateDirectories(extractedDir))
            {
                if (File.Exists(Path.Combine(subDir, "bin", "java.exe")))
                    return subDir;

                // 再深一层
                foreach (string subSubDir in Directory.EnumerateDirectories(subDir))
                {
                    if (File.Exists(Path.Combine(subSubDir, "bin", "java.exe")))
                        return subSubDir;
                }
            }

            return extractedDir;
        }

        /// <summary>
        /// 尝试通过 release 文件读取完整版本号。
        /// </summary>
        private static string? TryReadVersion(string jdkHome)
        {
            string releaseFile = Path.Combine(jdkHome, "release");
            if (!File.Exists(releaseFile)) return null;

            try
            {
                foreach (string line in File.ReadAllLines(releaseFile))
                {
                    // JAVA_VERSION="21.0.4"
                    if (line.StartsWith("JAVA_VERSION=", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = line["JAVA_VERSION=".Length..].Trim().Trim('"');
                        if (!string.IsNullOrEmpty(val))
                            return val;
                    }
                }
            }
            catch { /* best effort */ }

            return null;
        }
    }
}