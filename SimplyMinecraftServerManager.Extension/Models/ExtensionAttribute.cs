// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 标记在扩展程序集上，提供元数据信息。
/// 宿主通过此特性发现和加载扩展，无需额外清单文件。
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ExtensionAttribute : Attribute
{
    /// <summary>扩展的唯一标识符</summary>
    public required string Id { get; init; }

    /// <summary>扩展的显示名称</summary>
    public required string Name { get; init; }

    /// <summary>扩展的描述信息</summary>
    public string Description { get; init; } = "";

    /// <summary>扩展版本</summary>
    public required string Version { get; init; }

    /// <summary>作者列表</summary>
    public string[] Authors { get; init; } = [];

    /// <summary>扩展所需最低宿主 API 版本</summary>
    public string? HostApiVersion { get; init; }

    /// <summary>扩展的依赖（格式: "extensionId" 或 "extensionId>=1.0.0"）</summary>
    public string[] Dependencies { get; init; } = [];

    /// <summary>扩展项目主页</summary>
    public string? Website { get; init; }

    /// <summary>扩展分类标签</summary>
    public string[] Tags { get; init; } = [];
}
