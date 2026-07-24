// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models
{
    /// <summary>
    /// 描述扩展依赖的信息，包括依赖的扩展标识符和版本范围约束。
    /// </summary>
    /// <param name="Id">依赖扩展的唯一标识符</param>
    /// <param name="Range">依赖扩展的版本范围约束，null 表示无版本限制</param>
    public readonly record struct DependencyInfo(
        string Id,
        VersionRange? Range
    );
}
