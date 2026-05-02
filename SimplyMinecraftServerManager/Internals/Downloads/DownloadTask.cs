using SimplyMinecraftServerManager.Models;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 下载任务状态枚举。
    /// </summary>
    public enum DownloadStatus
    {
        Pending,
        Downloading,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// 任务类型枚举。
    /// </summary>
    public enum TaskType
    {
        Download,          // 仅下载
        DownloadAndInstall // 下载并安装
    }

    /// <summary>
    /// 安装状态枚举。
    /// </summary>
    public enum InstallationStatus
    {
        NotStarted,        // 未开始
        Installing,        // 安装中
        Installed,         // 安装成功
        InstallationFailed // 安装失败
    }

    /// <summary>
    /// 表示一个下载任务。
    /// </summary>
    public record class DownloadTask
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

        /// <summary>任务类型</summary>
        public TaskType Type { get; init; } = TaskType.Download;

        /// <summary>安装状态</summary>
        public InstallationStatus InstallationStatus { get; internal set; } = InstallationStatus.NotStarted;

        /// <summary>目标实例ID（用于安装任务）</summary>
        public string? TargetInstanceId { get; init; }

        /// <summary>任务创建时显示的通知</summary>
        public TaskNotificationMessage? CreatedNotification { get; init; }

        /// <summary>是否显示任务创建通知</summary>
        public bool NotifyOnCreated { get; init; } = true;

        /// <summary>任务完成时显示的通知</summary>
        public TaskNotificationMessage? CompletedNotification { get; init; }

        /// <summary>是否显示任务完成通知</summary>
        public bool NotifyOnCompleted { get; init; } = true;

        /// <summary>任务失败时显示的通知</summary>
        public TaskNotificationMessage? FailedNotification { get; init; }

        /// <summary>是否显示任务失败通知</summary>
        public bool NotifyOnFailed { get; init; } = true;

        /// <summary>已下载字节数</summary>
        public long BytesDownloaded { get; internal set; }

        /// <summary>总字节数</summary>
        public long TotalBytes { get; internal set; } = -1;

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; internal set; }

        /// <summary>取消令牌</summary>
        public CancellationTokenSource Cts { get; internal set; } = new();

        /// <summary>暂停时已下载的字节位置，用于恢复下载</summary>
        public long PausedPosition { get; internal set; }

        /// <summary>是否支持断点续传（服务器支持 Range 请求）</summary>
        public bool IsResumable { get; internal set; }

        /// <summary>开始时间</summary>
        public DateTime? StartTime { get; internal set; }

        /// <summary>完成时间</summary>
        public DateTime? EndTime { get; internal set; }

        /// <summary>安装开始时间</summary>
        public DateTime? InstallationStartTime { get; internal set; }

        /// <summary>安装完成时间</summary>
        public DateTime? InstallationEndTime { get; internal set; }
    }
}
