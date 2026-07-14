// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Net.Http;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// Leaf 服务端提供者。
    /// API 兼容 PaperMC v2 格式。
    /// https://api.leafmc.one/v2/projects/leaf
    /// </summary>
    public class LeafProvider : IServerProvider
    {
        private readonly PaperV2ApiProvider _inner;

        /// <inheritdoc />
        public ServerPlatform Platform => _inner.Platform;

        public LeafProvider(HttpClient? httpClient = null)
        {
            _inner = new PaperV2ApiProvider(
                ServerPlatform.Leaf,
                "https://api.leafmc.one/v2/projects/leaf",
                "leaf",
                "leaf",
                httpClient);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken ct = default)
            => _inner.GetVersionsAsync(ct);

        /// <inheritdoc />
        public Task<IReadOnlyList<ServerBuild>> GetBuildsAsync(string minecraftVersion, CancellationToken ct = default)
            => _inner.GetBuildsAsync(minecraftVersion, ct);

        /// <inheritdoc />
        public Task<ServerBuild?> GetLatestBuildAsync(string minecraftVersion, CancellationToken ct = default)
            => _inner.GetLatestBuildAsync(minecraftVersion, ct);

        /// <inheritdoc />
        public Task<DownloadTask> DownloadAsync(ServerBuild build, string destinationPath,
            DownloadManager? downloadManager = null, CancellationToken ct = default)
            => _inner.DownloadAsync(build, destinationPath, downloadManager, ct);
    }
}
