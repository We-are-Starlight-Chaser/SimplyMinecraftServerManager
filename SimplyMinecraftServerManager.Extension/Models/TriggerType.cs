// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 扩展触发器类型。
/// 定义扩展可以在哪些事件/条件下被触发执行。
/// </summary>
[Flags]
public enum TriggerType
{
    /// <summary>无触发器（默认）</summary>
    None = 0,

    /// <summary>应用启动后触发（仅一次）</summary>
    AppStartup = 1 << 0,

    /// <summary>应用关闭前触发</summary>
    AppShutdown = 1 << 1,

    /// <summary>服务器启动后触发</summary>
    ServerStarted = 1 << 2,

    /// <summary>服务器停止后触发</summary>
    ServerStopped = 1 << 3,

    /// <summary>玩家加入服务器时触发</summary>
    PlayerJoined = 1 << 4,

    /// <summary>玩家离开服务器时触发</summary>
    PlayerLeft = 1 << 5,

    /// <summary>插件安装时触发</summary>
    PluginInstalled = 1 << 6,

    /// <summary>插件卸载时触发</summary>
    PluginUninstalled = 1 << 7,

    /// <summary>配置文件变更时触发</summary>
    ConfigChanged = 1 << 8,

    /// <summary>定时触发（需指定 Cron 表达式或间隔）</summary>
    Timer = 1 << 9,

    /// <summary>自定义命令触发（扩展注册斜杠命令）</summary>
    Command = 1 << 10,

    /// <summary>内存使用超过阈值时触发</summary>
    MemoryThreshold = 1 << 11,

    /// <summary>服务器日志匹配指定模式时触发</summary>
    LogPattern = 1 << 12,
}
