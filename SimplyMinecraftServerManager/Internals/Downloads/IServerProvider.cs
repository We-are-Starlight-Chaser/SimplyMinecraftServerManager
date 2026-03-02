namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 服务端下载提供者统一接口。
    /// </summary>
    public interface IServerProvider
    {
        /// <summary>平台标识</summary>
        ServerPlatform Platform { get; }

        /// <summary>获取所有支持的 Minecraft 版本列表（降序）。</summary>
        Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default);

        /// <summary>获取指定版本的所有构建。</summary>
        Task<IReadOnlyList<ServerBuild>> GetBuildsAsync(string minecraftVersion, CancellationToken ct = default);

        /// <summary>获取指定版本的最新构建。</summary>
        Task<ServerBuild?> GetLatestBuildAsync(string minecraftVersion, CancellationToken ct = default);

        /// <summary>
        /// 下载指定构建到目标路径。
        /// </summary>
        Task<DownloadTask> DownloadAsync(
            ServerBuild build,
            string destinationPath,
            DownloadManager? downloadManager = null,
            CancellationToken ct = default);
    }
}