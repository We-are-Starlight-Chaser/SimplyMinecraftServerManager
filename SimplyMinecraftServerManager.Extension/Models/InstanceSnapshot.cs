// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 服务器实例的只读快照，供扩展读取。
/// 与主项目的 InstanceInfo 解耦，避免扩展直接依赖内部类型。
/// </summary>
public sealed record InstanceSnapshot
{
    /// <summary>实例唯一标识 (UUID)</summary>
    public required string Id { get; init; }

    /// <summary>显示名称</summary>
    public required string Name { get; init; }

    /// <summary>服务端 JAR 文件名（相对实例目录）</summary>
    public required string ServerJar { get; init; }

    /// <summary>Java 路径（留空表示使用全局默认）</summary>
    public required string JdkPath { get; init; }

    /// <summary>最小内存 MB</summary>
    public required int MinMemoryMb { get; init; }

    /// <summary>最大内存 MB</summary>
    public required int MaxMemoryMb { get; init; }

    /// <summary>额外 JVM 参数</summary>
    public required string ExtraJvmArgs { get; init; }

    /// <summary>创建时间 ISO-8601</summary>
    public required string CreatedAt { get; init; }

    /// <summary>实例在磁盘上的绝对路径</summary>
    public required string InstancePath { get; init; }
}
