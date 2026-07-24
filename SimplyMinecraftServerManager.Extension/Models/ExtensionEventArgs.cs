// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 服务器状态变更事件参数。
/// </summary>
public sealed class ServerStateChangedEventArgs : EventArgs
{
    public required string InstanceId { get; init; }
    public required ServerStateInfo State { get; init; }
}

/// <summary>
/// 插件/模组安装/卸载事件参数。
/// </summary>
public sealed class PluginEventArgs : EventArgs
{
    public required string InstanceId { get; init; }
    public required string PluginName { get; init; }
    public required bool IsInstalled { get; init; }
}

/// <summary>
/// 服务器配置变更事件参数。
/// </summary>
public sealed class ConfigChangedEventArgs : EventArgs
{
    public required string InstanceId { get; init; }
    public required string Key { get; init; }
    public string? Value { get; init; }
}
