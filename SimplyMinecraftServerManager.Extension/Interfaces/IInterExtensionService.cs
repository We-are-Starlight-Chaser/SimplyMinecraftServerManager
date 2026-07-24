// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 跨扩展安全通信服务。
/// 扩展之间通过此接口发送/接收消息，禁止直接反射调用其他扩展的方法。
///
/// 安全约束：
///   - 禁止自发自收（防递归注入）
///   - 消息纯文本，最大 4KB
///   - 全局限频 30 条/分钟 + 每目标限频 10 条/分钟
///   - 禁止控制字符和 null 字节（防注入）
///   - 广播最多发给 16 个扩展（防洪泛）
///   - 所有消息写入审计日志（最近 500 条）
///   - 终止状态的扩展无法收发消息
/// </summary>
public interface IInterExtensionService
{
    /// <summary>
    /// 向指定扩展发送字符串消息。
    /// </summary>
    /// <param name="targetExtensionId">目标扩展 ID（不能是自身）</param>
    /// <param name="message">消息内容（纯文本，最大 4KB）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>消息是否被目标扩展接受</returns>
    Task<bool> SendMessageAsync(string targetExtensionId, string message, CancellationToken ct = default);

    /// <summary>
    /// 向所有已加载的扩展广播字符串消息。最多发送给 16 个扩展。
    /// </summary>
    /// <param name="message">消息内容（纯文本，最大 4KB）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>接受消息的扩展数量</returns>
    Task<int> BroadcastMessageAsync(string message, CancellationToken ct = default);

    /// <summary>
    /// 注册消息接收回调。同一扩展只能注册一个回调。
    /// </summary>
    /// <param name="handler">消息处理器 (senderExtensionId, message)</param>
    /// <returns>用于取消注册的 IDisposable</returns>
    IDisposable OnMessageReceived(Func<string, string, Task> handler);

    /// <summary>
    /// 获取最近的消息审计日志（最近 500 条）。
    /// </summary>
    IReadOnlyList<MessageAuditEntry> GetAuditLog();
}

/// <summary>
/// 消息审计日志条目。
/// </summary>
public sealed class MessageAuditEntry
{
    /// <summary>消息发送时间（UTC）</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>发送方扩展 ID</summary>
    public string SenderId { get; init; } = "";

    /// <summary>接收方扩展 ID（"*" 表示广播）</summary>
    public string TargetId { get; init; } = "";

    /// <summary>消息大小（字节）</summary>
    public int MessageSize { get; init; }

    /// <summary>是否被拒绝</summary>
    public bool Rejected { get; init; }

    /// <summary>拒绝原因（如有）</summary>
    public string? RejectReason { get; init; }
}
