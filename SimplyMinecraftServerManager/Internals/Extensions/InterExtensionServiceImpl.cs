// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IInterExtensionService 实现，多层安全防护。
///
/// 防护层级：
///   1. 自发自收拦截 → 防递归注入
///   2. 终止状态拦截 → 被杀扩展无法收发
///   3. 内容安全扫描 → 控制字符 / null 字节拦截
///   4. 大小限制 → 4KB 上限
///   5. 目标存在性校验 → 发给不存在的扩展直接拒绝
///   6. 全局限频 → 30 条/分钟
///   7. 每目标限频 → 10 条/分钟
///   8. 广播上限 → 最多 16 个目标
///   9. Handler 唯一注册 → 防止多个处理器冲突
///  10. 审计日志 → 最近 500 条记录
/// </summary>
internal sealed class InterExtensionServiceImpl(string extensionId, ILogger logger, Func<bool>? isTerminated = null) : IInterExtensionService
{
    private readonly string _extensionId = extensionId;
    private readonly ILogger _logger = logger;
    private readonly Func<bool> _isTerminated = isTerminated ?? (() => false);
    private readonly ConcurrentDictionary<string, int> _perTargetCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _handlerLock = new();
    private Func<string, string, Task>? _handler;

    // 全局限频
    private int _globalCount;
    private DateTime _globalWindowStart = DateTime.UtcNow;

    // 审计日志（环形缓冲）
    private readonly Lock _auditLock = new();
    private readonly List<MessageAuditEntry> _auditLog = new(500);
    private const int MaxAuditEntries = 500;

    // 安全常量
    private const int MaxMessageSize = 4096;
    private const int MaxGlobalPerMinute = 30;
    private const int MaxPerTargetPerMinute = 10;
    private const int MaxBroadcastTargets = 16;

    /// <summary>由宿主注入：校验目标扩展是否存在且在线</summary>
    public Func<string, bool>? IsExtensionLoaded { get; set; }

    /// <summary>由宿主注入：实际的定向消息投递</summary>
    public Func<string, string, string, Task<bool>>? SendMessageHandler { get; set; }

    /// <summary>由宿主注入：实际的广播投递</summary>
    public Func<string, string, Task<int>>? BroadcastHandler { get; set; }

    public async Task<bool> SendMessageAsync(string targetExtensionId, string message, CancellationToken ct = default)
    {
        // 1. 自发自收
        if (string.Equals(targetExtensionId, _extensionId, StringComparison.OrdinalIgnoreCase))
        {
            RecordAudit(targetExtensionId, message.Length, true, "禁止自发自收");
            _logger.Warn($"[{_extensionId}] 尝试自发自收，已拒绝");
            return false;
        }

        // 2. 终止状态
        if (_isTerminated())
        {
            RecordAudit(targetExtensionId, message.Length, true, "扩展已终止");
            return false;
        }

        // 3. 内容安全
        if (!IsMessageContentSafe(message))
        {
            RecordAudit(targetExtensionId, message.Length, true, "消息包含非法字符");
            _logger.Warn($"[{_extensionId}] 消息包含控制字符或 null 字节，已拒绝");
            return false;
        }

        // 4. 大小限制
        if (message.Length > MaxMessageSize)
        {
            RecordAudit(targetExtensionId, MaxMessageSize, true, $"消息过大: {message.Length}");
            _logger.Warn($"[{_extensionId}] 消息过大: {message.Length} > {MaxMessageSize}");
            return false;
        }

        // 5. 目标存在性
        if (IsExtensionLoaded is not null && !IsExtensionLoaded.Invoke(targetExtensionId))
        {
            RecordAudit(targetExtensionId, message.Length, true, "目标扩展不存在或未加载");
            _logger.Warn($"[{_extensionId}] 目标扩展 {targetExtensionId} 不存在或未加载");
            return false;
        }

        // 6. 全局限频
        if (!TryIncrementGlobal())
        {
            RecordAudit(targetExtensionId, message.Length, true, $"全局限频: >{MaxGlobalPerMinute}/min");
            _logger.Warn($"[{_extensionId}] 全局发送频率超限 ({MaxGlobalPerMinute}/min)");
            return false;
        }

        // 7. 每目标限频
        if (!TryIncrementPerTarget(targetExtensionId))
        {
            RecordAudit(targetExtensionId, message.Length, true, $"目标限频: >{MaxPerTargetPerMinute}/min → {targetExtensionId}");
            _logger.Warn($"[{_extensionId}] → {targetExtensionId} 频率超限 ({MaxPerTargetPerMinute}/min)");
            return false;
        }

        _logger.Info($"[{_extensionId}] → {targetExtensionId}: {message.Length} bytes");
        RecordAudit(targetExtensionId, message.Length, false, null);

        if (SendMessageHandler is not null)
            return await SendMessageHandler.Invoke(_extensionId, targetExtensionId, message).ConfigureAwait(false);

        return false;
    }

    public async Task<int> BroadcastMessageAsync(string message, CancellationToken ct = default)
    {
        // 1. 终止状态
        if (_isTerminated())
        {
            RecordAudit("*", message.Length, true, "扩展已终止");
            return 0;
        }

        // 2. 内容安全
        if (!IsMessageContentSafe(message))
        {
            RecordAudit("*", message.Length, true, "消息包含非法字符");
            _logger.Warn($"[{_extensionId}] 广播消息包含非法字符，已拒绝");
            return 0;
        }

        // 3. 大小限制
        if (message.Length > MaxMessageSize)
        {
            RecordAudit("*", MaxMessageSize, true, $"消息过大: {message.Length}");
            return 0;
        }

        // 4. 全局限频
        if (!TryIncrementGlobal())
        {
            RecordAudit("*", message.Length, true, $"全局限频: >{MaxGlobalPerMinute}/min");
            _logger.Warn($"[{_extensionId}] 广播频率超限 ({MaxGlobalPerMinute}/min)");
            return 0;
        }

        _logger.Info($"[{_extensionId}] 广播: {message.Length} bytes");
        RecordAudit("*", message.Length, false, null);

        if (BroadcastHandler is not null)
            return await BroadcastHandler.Invoke(_extensionId, message).ConfigureAwait(false);

        return 0;
    }

    public IDisposable OnMessageReceived(Func<string, string, Task> handler)
    {
        lock (_handlerLock)
        {
            if (_handler is not null)
            {
                _logger.Warn($"[{_extensionId}] 重复注册消息处理器，已拒绝");
                throw new InvalidOperationException("同一扩展只能注册一个消息处理器");
            }
            _handler = handler;
        }

        return new DisposableAction(() =>
        {
            lock (_handlerLock)
            {
                _handler = null;
            }
        });
    }

    public IReadOnlyList<MessageAuditEntry> GetAuditLog()
    {
        lock (_auditLock)
        {
            return [.. _auditLog];
        }
    }

    internal async Task DispatchMessageAsync(string fromExtensionId, string message)
    {
        Func<string, string, Task>? handler;
        lock (_handlerLock)
        {
            handler = _handler;
        }

        if (handler is not null)
        {
            try
            {
                await handler.Invoke(fromExtensionId, message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"[{_extensionId}] 消息处理异常（来自 {fromExtensionId}）", ex);
            }
        }
    }

    // ======================== 内部方法 ========================

    private static bool IsMessageContentSafe(string message)
    {
        foreach (char c in message)
        {
            if (c == '\0') return false;
            if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t') return false;
        }
        return true;
    }

    private bool TryIncrementGlobal()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _globalWindowStart).TotalSeconds;

        if (elapsed >= 60)
        {
            Interlocked.Exchange(ref _globalCount, 0);
            _globalWindowStart = now;
        }

        int count = Interlocked.Increment(ref _globalCount);
        return count <= MaxGlobalPerMinute;
    }

    private bool TryIncrementPerTarget(string targetId)
    {
        int count = _perTargetCounts.AddOrUpdate(targetId, 1, (_, v) => v + 1);
        return count <= MaxPerTargetPerMinute;
    }

    private void RecordAudit(string targetId, int messageSize, bool rejected, string? rejectReason)
    {
        var entry = new MessageAuditEntry
        {
            Timestamp = DateTime.UtcNow,
            SenderId = _extensionId,
            TargetId = targetId,
            MessageSize = messageSize,
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

    private sealed class DisposableAction(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}
