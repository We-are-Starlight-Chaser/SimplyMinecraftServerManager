namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 下载进度信息。
    /// </summary>
    public class DownloadProgressInfo : EventArgs
    {
        /// <summary>任务唯一 ID</summary>
        public string TaskId { get; init; } = "";

        /// <summary>显示名称</summary>
        public string DisplayName { get; init; } = "";

        /// <summary>已下载字节数</summary>
        public long BytesDownloaded { get; init; }

        /// <summary>总字节数（-1 表示未知）</summary>
        public long TotalBytes { get; init; } = -1;

        /// <summary>进度百分比 0~100（总大小未知时为 -1）</summary>
        public double ProgressPercent =>
            TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100.0 : -1;

        /// <summary>当前下载速度 (bytes/s)</summary>
        public long SpeedBytesPerSecond { get; init; }

        /// <summary>是否已完成</summary>
        public bool IsCompleted { get; init; }

        /// <summary>是否失败</summary>
        public bool IsFailed { get; init; }

        /// <summary>是否已暂停</summary>
        public bool IsPaused { get; init; }

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; init; }
    }
}