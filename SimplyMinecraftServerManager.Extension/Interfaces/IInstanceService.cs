// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 服务器实例读取服务，供扩展查询和浏览实例信息。
/// 扩展不应通过此接口修改实例数据。
/// </summary>
public interface IInstanceService
{
    /// <summary>获取所有已注册实例的快照</summary>
    IReadOnlyList<InstanceSnapshot> GetAll();

    /// <summary>根据 ID 获取单个实例快照，不存在则返回 null</summary>
    InstanceSnapshot? GetById(string instanceId);

    /// <summary>按名称或 ID 搜索匹配的实例</summary>
    IReadOnlyList<InstanceSnapshot> Search(string query);

    /// <summary>获取指定实例的根目录路径</summary>
    string? GetInstancePath(string instanceId);

    /// <summary>获取指定实例的插件目录路径（如不存在返回 null）</summary>
    string? GetPluginsPath(string instanceId);

    /// <summary>获取指定实例的配置目录路径（如不存在返回 null）</summary>
    string? GetConfigPath(string instanceId);
}
