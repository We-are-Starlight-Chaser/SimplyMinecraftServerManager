namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 某个服务端的一次构建信息。
    /// </summary>
    public class ServerBuild
    {
        /// <summary>平台</summary>
        public ServerPlatform Platform { get; init; }

        /// <summary>Minecraft 版本号，如 "1.21.4"</summary>
        public string MinecraftVersion { get; init; } = "";

        /// <summary>构建号，如 123</summary>
        public int BuildNumber { get; init; }

        /// <summary>构建渠道 (default / experimental)</summary>
        public string Channel { get; init; } = "default";

        /// <summary>下载文件名</summary>
        public string FileName { get; init; } = "";

        /// <summary>完整下载 URL</summary>
        public string DownloadUrl { get; init; } = "";

        /// <summary>SHA-256 哈希（有些 API 提供）</summary>
        public string? Sha256 { get; init; }

        public override string ToString()
            => $"{Platform} {MinecraftVersion} build #{BuildNumber}";
    }
}