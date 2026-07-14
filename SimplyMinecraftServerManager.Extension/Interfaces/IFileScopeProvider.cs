// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 文件访问范围声明接口。
/// 扩展实现此接口声明所需的文件访问范围（除默认的数据目录外）。
/// 宿主在加载扩展时读取并创建 FileAccessGuard。
/// </summary>
public interface IFileScopeProvider
{
    /// <summary>
    /// 声明此扩展需要的文件访问范围列表。
    /// 默认的 "data" 范围（扩展数据目录）由宿主自动添加，无需重复声明。
    /// </summary>
    IReadOnlyList<FileAccessScope> DeclareFileScopes();
}
