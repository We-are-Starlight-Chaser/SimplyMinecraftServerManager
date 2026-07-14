// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 服务器进程控制服务，供扩展启动、停止和向服务器发送命令。
/// </summary>
public interface IServerService
{
    /// <summary>启动指定实例的服务器</summary>
    Task StartAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>停止指定实例的服务器（发送 stop 命令）</summary>
    Task StopAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>强制终止指定实例的服务器进程</summary>
    Task KillAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>向运行中的服务器发送控制台命令</summary>
    Task SendCommandAsync(string instanceId, string command, CancellationToken cancellationToken = default);

    /// <summary>通过 RCON 发送命令并获取响应（如果 RCON 可用）</summary>
    Task<string?> SendRconCommandAsync(string instanceId, string command, CancellationToken cancellationToken = default);

    /// <summary>获取指定实例的当前运行状态</summary>
    ServerStateInfo? GetState(string instanceId);

    /// <summary>获取指定实例的服务器日志（最近 N 行）</summary>
    IReadOnlyList<string> GetConsoleLog(string instanceId, int maxLines = 200);
}
