// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 扩展的元数据信息。
/// </summary>
public interface IExtensionMetadata
{
    /// <summary>扩展的唯一标识符（如 "myplugin.core"）</summary>
    string Id { get; }

    /// <summary>扩展的显示名称</summary>
    string Name { get; }

    /// <summary>扩展的描述信息</summary>
    string Description { get; }

    /// <summary>扩展的版本号</summary>
    Version Version { get; }

    /// <summary>作者列表</summary>
    string[] Authors { get; }

    /// <summary>
    /// 此扩展依赖的其他扩展及其版本约束。
    /// 空数组表示无依赖；null 元素应被视为无效配置。
    /// </summary>
    DependencyInfo[] Dependencies { get; }

    /// <summary>
    /// 扩展所需的最低宿主 API 版本。
    /// null 表示无版本要求。
    /// </summary>
    Version? HostApiVersion => null;

    /// <summary>扩展的项目主页或仓库地址</summary>
    string? Website => null;

    /// <summary>扩展的分类标签，用于搜索和过滤</summary>
    string[] Tags => [];
}
