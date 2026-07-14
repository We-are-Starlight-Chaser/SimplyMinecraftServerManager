// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IInstanceService 实现，桥接到 InstanceManager。
/// 将 InstanceInfo 转换为 InstanceSnapshot 提供给扩展。
/// 所有方法在执行前检查扩展是否拥有 InstanceAccess 能力。
/// </summary>
internal sealed class InstanceServiceImpl(CapabilityGuard? guard = null) : IInstanceService
{
    private readonly CapabilityGuard? _guard = guard;

    public IReadOnlyList<InstanceSnapshot> GetAll()
    {
        _guard?.Ensure(ExtensionCapability.InstanceAccess, "GetAll");

        return InstanceManager.GetAll()
            .Select(MapToSnapshot)
            .ToList();
    }

    public InstanceSnapshot? GetById(string instanceId)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(instanceId);
        _guard?.Ensure(ExtensionCapability.InstanceAccess, "GetById");

        return InstanceManager.GetById(instanceId) is { } info
            ? MapToSnapshot(info)
            : null;
    }

    public IReadOnlyList<InstanceSnapshot> Search(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        _guard?.Ensure(ExtensionCapability.InstanceAccess, "Search");

        return InstanceManager.Search(query)
            .Select(MapToSnapshot)
            .ToList();
    }

    public string? GetInstancePath(string instanceId)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(instanceId);
        _guard?.Ensure(ExtensionCapability.InstanceAccess, "GetInstancePath");

        return InstanceManager.GetById(instanceId) is not null
            ? Path.Combine(PathHelper.InstancesRoot, instanceId)
            : null;
    }

    public string? GetPluginsPath(string instanceId)
    {
        string? instancePath = GetInstancePath(instanceId);
        if (instancePath is null) return null;

        string pluginsPath = Path.Combine(instancePath, "plugins");
        return Directory.Exists(pluginsPath) ? pluginsPath : null;
    }

    public string? GetConfigPath(string instanceId)
    {
        string? instancePath = GetInstancePath(instanceId);
        if (instancePath is null) return null;

        string configPath = Path.Combine(instancePath, "config");
        return Directory.Exists(configPath) ? configPath : null;
    }

    private static InstanceSnapshot MapToSnapshot(InstanceInfo info) => new()
    {
        Id = info.Id,
        Name = info.Name,
        ServerJar = info.ServerJar,
        JdkPath = info.JdkPath,
        MinMemoryMb = info.MinMemoryMb,
        MaxMemoryMb = info.MaxMemoryMb,
        ExtraJvmArgs = info.ExtraJvmArgs,
        CreatedAt = info.CreatedAt,
        InstancePath = Path.Combine(PathHelper.InstancesRoot, info.Id)
    };
}
