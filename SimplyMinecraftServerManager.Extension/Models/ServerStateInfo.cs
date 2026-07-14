// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 服务器进程的运行状态信息。
/// </summary>
public sealed record ServerStateInfo
{
    /// <summary>关联的实例标识</summary>
    public required string InstanceId { get; init; }

    /// <summary>服务器是否正在运行</summary>
    public required bool IsRunning { get; init; }

    /// <summary>服务器进程 ID（未运行时为 null）</summary>
    public int? ProcessId { get; init; }

    /// <summary>服务器启动时间（未运行时为 null）</summary>
    public DateTimeOffset? StartedAt { get; init; }
}
