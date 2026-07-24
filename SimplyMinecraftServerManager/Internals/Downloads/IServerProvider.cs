// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

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

        /// <summary>
        /// 下载完成后的后处理钩子。
        /// 默认实现直接返回下载路径（大多数平台无需后处理）。
        /// NeoForge 需要运行 installer 生成实际的 server.jar。
        /// </summary>
        /// <param name="downloadedFilePath">已下载的文件路径</param>
        /// <param name="workingDirectory">实例工作目录</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>最终可用的服务器 JAR 路径</returns>
        Task<string> AfterDownloadAsync(
            string downloadedFilePath,
            string workingDirectory,
            CancellationToken ct = default)
        {
            return Task.FromResult(downloadedFilePath);
        }
    }
}