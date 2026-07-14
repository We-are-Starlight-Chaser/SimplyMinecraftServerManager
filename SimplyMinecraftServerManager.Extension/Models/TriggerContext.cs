// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 触发器执行上下文，传递触发时的环境信息。
/// </summary>
public sealed class TriggerContext
{
    /// <summary>触发器类型</summary>
    public required TriggerType TriggerType { get; init; }

    /// <summary>触发时间</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>关联的实例 ID（可选）</summary>
    public string? InstanceId { get; init; }

    /// <summary>触发来源描述（如 "player:Steve", "command:reload", "timer:5m"）</summary>
    public string? Source { get; init; }

    /// <summary>
    /// 附加数据。
    /// 不同触发类型携带不同数据：
    ///   - PlayerJoined/Left:  { "playerName": "Steve" }
    ///   - LogPattern:         { "matchedLine": "ERROR ..." }
    ///   - MemoryThreshold:    { "currentMb": "4096" }
    /// </summary>
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
}
