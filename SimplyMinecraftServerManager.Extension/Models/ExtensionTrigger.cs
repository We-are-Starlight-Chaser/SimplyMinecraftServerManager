// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 扩展触发器定义。
/// 描述一个触发条件及其附加参数。
/// </summary>
public sealed class ExtensionTrigger
{
    /// <summary>触发器类型</summary>
    public required TriggerType Type { get; init; }

    /// <summary>
    /// 触发器优先级（数值越小越先执行）。
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// 是否在触发后自动注销（一次性触发器）。
    /// </summary>
    public bool Once { get; init; }

    /// <summary>
    /// 附加条件参数。
    /// 不同 Type 对应不同参数格式：
    ///   - Timer:           { "interval": "5m" } 或 { "cron": "0 */5 * * * *" }
    ///   - Command:         { "command": "reload" }
    ///   - MemoryThreshold: { "thresholdMb": 4096, "instanceId": "xxx" }
    ///   - LogPattern:      { "pattern": "ERROR.*OutOfMemory", "instanceId": "xxx" }
    ///   - PlayerJoined:    { "instanceId": "xxx" }（空表示所有实例）
    /// </summary>
    public Dictionary<string, string> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 限制条件：仅当此表达式求值为 true 时才触发。
    /// 支持简单的布尔表达式（扩展自定义解析）。
    /// </summary>
    public string? Condition { get; init; }
}
