// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// INotificationService 实现，多层安全防护。
///
/// 防护层级：
///   1. 终止状态拦截
///   2. 内容安全扫描 → null 字节 / 控制字符 / 空值拦截
///   3. 标题自动前缀 → "[ext-id] 原标题"（防伪装系统通知）
///   4. 大小限制 → 标题 100 字符，消息 2000 字符
///   5. 时长夹紧 → 1000ms - 15000ms
///   6. 全局限频 → 10 条/分钟
///   7. 待显示队列上限 → 最多 3 条
///   8. 确认弹窗唯一 → 同时只能有 1 个确认弹窗
///   9. 审计日志 → 最近 200 条
/// </summary>
internal sealed class NotificationServiceImpl(string extensionId, ILogger logger, Func<bool>? isTerminated = null) : INotificationService
{
    private readonly string _extensionId = extensionId;
    private readonly ILogger _logger = logger;
    private readonly Func<bool> _isTerminated = isTerminated ?? (() => false);

    // 频率限制
    private int _count;
    private DateTime _windowStart = DateTime.UtcNow;

    // 待显示队列
    private int _pendingCount;
    private int _pendingConfirms;

    // 审计日志
    private readonly Lock _auditLock = new();
    private readonly List<NotificationAuditEntry> _auditLog = new(200);
    private const int MaxAuditEntries = 200;

    // 安全常量
    private const int MaxTitleLength = 20;
    private const int MaxMessageLength = 200;
    private const int MinDurationMs = 1000;
    private const int MaxDurationMs = 15000;
    private const int MaxPerMinute = 10;
    private const int MaxPending = 3;
    private const int MaxPendingConfirms = 1;

    public event Func<string, string, INotificationService.NotificationLevel, int, Task>? OnNotificationRequested;
    public event Func<string, string, Task<bool>>? OnConfirmRequested;

    public void Show(string title, string message, INotificationService.NotificationLevel level = INotificationService.NotificationLevel.Info, int durationMs = 3000)
    {
        // 1. 终止状态
        if (_isTerminated())
        {
            RecordAudit(level, true, "扩展已终止");
            return;
        }

        // 2. 空值检查
        if (string.IsNullOrWhiteSpace(title))
        {
            RecordAudit(level, true, "标题为空");
            _logger.Warn($"[{_extensionId}] 通知标题为空，已拒绝");
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            RecordAudit(level, true, "消息为空");
            _logger.Warn($"[{_extensionId}] 通知消息为空，已拒绝");
            return;
        }

        // 3. 内容安全
        if (!IsContentSafe(title) || !IsContentSafe(message))
        {
            RecordAudit(level, true, "内容包含非法字符");
            _logger.Warn($"[{_extensionId}] 通知内容包含非法字符，已拒绝");
            return;
        }

        // 4. 时长夹紧
        durationMs = Math.Clamp(durationMs, MinDurationMs, MaxDurationMs);

        // 5. 全局限频
        if (!TryIncrement())
        {
            RecordAudit(level, true, $"限频: >{MaxPerMinute}/min");
            _logger.Warn($"[{_extensionId}] 通知频率超限 ({MaxPerMinute}/min)");
            return;
        }

        // 6. 待显示队列上限
        if (Interlocked.Increment(ref _pendingCount) > MaxPending)
        {
            Interlocked.Decrement(ref _pendingCount);
            RecordAudit(level, true, $"待显示队列满: >{MaxPending}");
            _logger.Warn($"[{_extensionId}] 通知待显示队列已满 ({MaxPending})");
            return;
        }

        // 7. 格式化：自动附加扩展 ID 前缀
        title = $"[{_extensionId}] {Truncate(title, MaxTitleLength - _extensionId.Length - 4)}";
        message = Truncate(message, MaxMessageLength);

        _logger.Info($"[{_extensionId}] 通知 ({level}): {title} - {message}");
        RecordAudit(level, false, null);

        if (OnNotificationRequested is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await OnNotificationRequested.Invoke(title, message, level, durationMs).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingCount);
                }
            });
        }
        else
        {
            Interlocked.Decrement(ref _pendingCount);
        }
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, CancellationToken ct = default)
    {
        // 1. 终止状态
        if (_isTerminated())
        {
            RecordAudit(INotificationService.NotificationLevel.Info, true, "扩展已终止");
            return false;
        }

        // 2. 空值检查
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            RecordAudit(INotificationService.NotificationLevel.Info, true, "标题或消息为空");
            return false;
        }

        // 3. 内容安全
        if (!IsContentSafe(title) || !IsContentSafe(message))
        {
            RecordAudit(INotificationService.NotificationLevel.Info, true, "内容包含非法字符");
            return false;
        }

        // 4. 唯一确认弹窗
        if (Interlocked.Increment(ref _pendingConfirms) > MaxPendingConfirms)
        {
            Interlocked.Decrement(ref _pendingConfirms);
            RecordAudit(INotificationService.NotificationLevel.Info, true, "已有待处理的确认弹窗");
            _logger.Warn($"[{_extensionId}] 已有待处理的确认弹窗，拒绝新弹窗");
            return false;
        }

        try
        {
            title = $"[{_extensionId}] {Truncate(title, MaxTitleLength - _extensionId.Length - 4)}";
            message = Truncate(message, MaxMessageLength);

            RecordAudit(INotificationService.NotificationLevel.Info, false, null);

            if (OnConfirmRequested is not null)
                return await OnConfirmRequested.Invoke(title, message).ConfigureAwait(false);

            return false;
        }
        finally
        {
            Interlocked.Decrement(ref _pendingConfirms);
        }
    }

    public IReadOnlyList<NotificationAuditEntry> GetAuditLog()
    {
        lock (_auditLock)
        {
            return [.. _auditLog];
        }
    }

    // ======================== 内部方法 ========================

    private static bool IsContentSafe(string value)
    {
        foreach (char c in value)
        {
            if (c == '\0') return false;
            if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t') return false;
        }
        return true;
    }

    private bool TryIncrement()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _windowStart).TotalSeconds;

        if (elapsed >= 60)
        {
            Interlocked.Exchange(ref _count, 0);
            _windowStart = now;
        }

        int c = Interlocked.Increment(ref _count);
        return c <= MaxPerMinute;
    }

    private void RecordAudit(INotificationService.NotificationLevel level, bool rejected, string? rejectReason)
    {
        var entry = new NotificationAuditEntry
        {
            Timestamp = DateTime.UtcNow,
            ExtensionId = _extensionId,
            Level = level,
            Rejected = rejected,
            RejectReason = rejectReason,
        };

        lock (_auditLock)
        {
            if (_auditLog.Count >= MaxAuditEntries)
                _auditLog.RemoveAt(0);
            _auditLog.Add(entry);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
