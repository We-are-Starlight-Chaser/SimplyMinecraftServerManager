
namespace SimplyMinecraftServerManager.Internals.Downloads.JDK
{
    /// <summary>
    /// 可下载的 JDK 版本信息。
    /// </summary>
    public class JdkInfo
    {
        /// <summary>发行版</summary>
        public JdkDistribution Distribution { get; init; }

        /// <summary>主版本号 (8 / 11 / 17 / 21 / 23 …)</summary>
        public int MajorVersion { get; init; }

        /// <summary>完整版本字符串 (21.0.4+7)</summary>
        public string FullVersion { get; init; } = "";

        /// <summary>CPU 架构</summary>
        public JdkArchitecture Architecture { get; init; }

        /// <summary>下载 URL</summary>
        public string DownloadUrl { get; init; } = "";

        /// <summary>文件名</summary>
        public string FileName { get; init; } = "";

        /// <summary>文件大小 (字节)，-1 表示未知</summary>
        public long FileSize { get; init; } = -1;

        /// <summary>SHA-256 校验值（如 API 提供）</summary>
        public string? Sha256 { get; init; }

        /// <summary>包类型 (zip / tar.gz / msi)</summary>
        public string PackageType { get; init; } = "zip";

        public override string ToString()
            => $"{Distribution} JDK {FullVersion} ({Architecture})";
    }
}