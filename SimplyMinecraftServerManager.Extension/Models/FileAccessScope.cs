// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 扩展声明的文件访问范围。
/// 在加载时声明，运行时由 FileAccessGuard 校验。
/// </summary>
public sealed class FileAccessScope
{
    /// <summary>范围标识（如 "config", "logs", "data"）</summary>
    public required string Id { get; init; }

    /// <summary>显示名称</summary>
    public required string Name { get; init; }

    /// <summary>请求的访问级别</summary>
    public required FileAccessLevel Level { get; init; }

    /// <summary>
    /// 允许访问的目录路径列表。
    /// 支持以下特殊路径：
    ///   - "${extensionData}"  → 扩展专属数据目录
    ///   - "${instance:{id}}" → 指定实例目录
    ///   - "${instanceRoot}"  → 所有实例根目录
    ///   - 绝对路径或相对路径
    /// </summary>
    public required string[] Paths { get; init; }

    /// <summary>
    /// 允许的文件扩展名过滤（空数组 = 允许所有）。
    /// 示例: [".toml", ".json", ".log"]
    /// </summary>
    public string[] AllowedExtensions { get; init; } = [];

    /// <summary>
    /// 禁止的文件扩展名过滤。
    /// 优先级高于 AllowedExtensions。
    /// </summary>
    public string[] DeniedExtensions { get; init; } = [".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh"];
}
