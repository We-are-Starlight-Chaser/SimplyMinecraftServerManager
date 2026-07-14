// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IServerService 实现，桥接到 ServerProcessManager 和 ServerProcess。
/// 所有方法在执行前检查扩展是否拥有 ServerControl 能力。
/// </summary>
internal sealed class ServerServiceImpl(CapabilityGuard? guard = null) : IServerService
{
    private readonly CapabilityGuard? _guard = guard;

    public Task StartAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(instanceId);
        _guard?.Ensure(ExtensionCapability.ServerControl, "StartAsync");

        if (ServerProcessManager.IsRunning(instanceId))
        {
            return Task.CompletedTask;
        }

        var instance = InstanceManager.GetById(instanceId)
            ?? throw new InvalidOperationException($"实例 '{instanceId}' 不存在");

        var process = new ServerProcess(instanceId);
        ServerProcessManager.Register(instanceId, process);

        return process.StartAsync(cancellationToken);
    }

    public Task StopAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(instanceId);
        _guard?.Ensure(ExtensionCapability.ServerControl, "StopAsync");

        if (!ServerProcessManager.IsRunning(instanceId))
        {
            return Task.CompletedTask;
        }

        ServerProcessManager.StopAndRemove(instanceId);
        return Task.CompletedTask;
    }

    public Task KillAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(instanceId);
        _guard?.Ensure(ExtensionCapability.ServerControl, "KillAsync");

        if (!ServerProcessManager.IsRunning(instanceId))
        {
            return Task.CompletedTask;
        }

        ServerProcessManager.KillAndRemove(instanceId);
        return Task.CompletedTask;
    }

    public Task SendCommandAsync(string instanceId, string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        _guard?.Ensure(ExtensionCapability.ServerControl, "SendCommandAsync");

        var process = ServerProcessManager.GetProcess(instanceId)
            ?? throw new InvalidOperationException($"实例 '{instanceId}' 未运行");

        process.SendCommand(command);
        return Task.CompletedTask;
    }

    public async Task<string?> SendRconCommandAsync(string instanceId, string command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        _guard?.Ensure(ExtensionCapability.ServerControl, "SendRconCommandAsync");

        var process = ServerProcessManager.GetProcess(instanceId)
            ?? throw new InvalidOperationException($"实例 '{instanceId}' 未运行");

        return await process.ExecuteRconCommandAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public ServerStateInfo? GetState(string instanceId)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(instanceId);
        _guard?.Ensure(ExtensionCapability.ServerControl, "GetState");

        var process = ServerProcessManager.GetProcess(instanceId);
        if (process is null) return null;

        return new ServerStateInfo
        {
            InstanceId = instanceId,
            IsRunning = process.IsRunning,
            ProcessId = process.ProcessId,
            StartedAt = process.IsRunning ? ServerProcessManager.GetStartTime(instanceId) : null
        };
    }

    public IReadOnlyList<string> GetConsoleLog(string instanceId, int maxLines = 200)
    {
        _guard?.Ensure(ExtensionCapability.ServerControl, "GetConsoleLog");

        // ServerProcess 不直接暴露历史日志，返回空列表
        // 扩展应通过 EventBus 订阅 OutputReceived 事件获取实时日志
        return Array.Empty<string>();
    }
}
