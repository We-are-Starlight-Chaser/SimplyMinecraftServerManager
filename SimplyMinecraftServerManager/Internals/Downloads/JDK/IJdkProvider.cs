namespace SimplyMinecraftServerManager.Internals.Downloads.JDK
{
    /// <summary>
    /// JDK 下载提供者统一接口。
    /// </summary>
    public interface IJdkProvider
    {
        JdkDistribution Distribution { get; }

        /// <summary>
        /// 获取所有可用的 LTS/GA 主版本号（降序）。
        /// </summary>
        Task<IReadOnlyList<int>> GetAvailableMajorVersionsAsync(
            CancellationToken ct = default);

        /// <summary>
        /// 获取指定主版本的所有可下载构建（降序，最新在前）。
        /// </summary>
        Task<IReadOnlyList<JdkInfo>> GetBuildsAsync(
            int majorVersion,
            JdkArchitecture? architecture = null,
            CancellationToken ct = default);

        /// <summary>
        /// 获取指定主版本的最新构建。
        /// </summary>
        Task<JdkInfo?> GetLatestAsync(
            int majorVersion,
            JdkArchitecture? architecture = null,
            CancellationToken ct = default);
    }
}