// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 通知服务，供扩展安全地向用户展示通知。
/// 扩展不能直接操作 UI，必须通过此接口。
///
/// 安全约束：
///   - 通知标题自动附加扩展 ID 前缀（防伪装系统通知）
///   - 禁止空标题 / 空消息
///   - 内容过滤控制字符和 null 字节
///   - 标题最大 20 字符，消息最大 200 字符
///   - 限频 10 条/分钟 + 最多 3 条待显示
///   - ShowConfirmAsync 需要 Notification | Confirmation 能力
///   - Warning / Error 级别需要更高权限
///   - 所有通知写入审计日志（最近 200 条）
///   - 终止状态的扩展无法发通知
/// </summary>
public interface INotificationService
{
    /// <summary>通知级别</summary>
    enum NotificationLevel
    {
        Info,
        Warning,
        Error,
        Success,
    }

    /// <summary>
    /// 显示一条通知。
    /// 标题将被自动格式化为 "[扩展ID] 原标题"。
    /// </summary>
    /// <param name="title">通知标题（1-20 字符，不能为空）</param>
    /// <param name="message">通知内容（1-200 字符，不能为空）</param>
    /// <param name="level">通知级别</param>
    /// <param name="durationMs">显示时长（毫秒），最小 1000，最大 15000</param>
    void Show(string title, string message, NotificationLevel level = NotificationLevel.Info, int durationMs = 3000);

    /// <summary>
    /// 显示一条带确认操作的通知。最多同时存在 1 个确认弹窗。
    /// </summary>
    /// <returns>用户是否确认</returns>
    Task<bool> ShowConfirmAsync(string title, string message, CancellationToken ct = default);

    /// <summary>
    /// 获取通知审计日志（最近 200 条）。
    /// </summary>
    IReadOnlyList<NotificationAuditEntry> GetAuditLog();
}

/// <summary>
/// 通知审计日志条目。
/// </summary>
public sealed class NotificationAuditEntry
{
    /// <summary>发送时间（UTC）</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>扩展 ID</summary>
    public string ExtensionId { get; init; } = "";

    /// <summary>通知级别</summary>
    public INotificationService.NotificationLevel Level { get; init; }

    /// <summary>是否被拒绝</summary>
    public bool Rejected { get; init; }

    /// <summary>拒绝原因</summary>
    public string? RejectReason { get; init; }
}
