namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 下载任务状态枚举。
    /// </summary>
    public enum DownloadStatus
    {
        Pending,
        Downloading,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// 表示一个下载任务。
    /// </summary>
    public class DownloadTask
    {
        /// <summary>任务唯一 ID</summary>
        public string Id { get; } = Guid.NewGuid().ToString("N");

        /// <summary>显示名称</summary>
        public string DisplayName { get; init; } = "";

        /// <summary>下载 URL</summary>
        public string Url { get; init; } = "";

        /// <summary>保存的本地完整路径</summary>
        public string DestinationPath { get; init; } = "";

        /// <summary>预期文件哈希 (SHA-1 / SHA-256)，可选</summary>
        public string? ExpectedHash { get; init; }

        /// <summary>哈希算法名称 ("SHA1" / "SHA256")</summary>
        public string HashAlgorithm { get; init; } = "SHA256";

        /// <summary>当前状态</summary>
        public DownloadStatus Status { get; internal set; } = DownloadStatus.Pending;

        /// <summary>已下载字节数</summary>
        public long BytesDownloaded { get; internal set; }

        /// <summary>总字节数</summary>
        public long TotalBytes { get; internal set; } = -1;

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; internal set; }

        /// <summary>取消令牌</summary>
        public CancellationTokenSource Cts { get; } = new();

        /// <summary>开始时间</summary>
        public DateTime? StartTime { get; internal set; }

        /// <summary>完成时间</summary>
        public DateTime? EndTime { get; internal set; }
    }
}