// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 服务器平台类型，定义了支持的各种 Minecraft 服务器分支。
    /// </summary>
    public enum ServerPlatform
    {
        /// <summary>
        /// Paper 服务器，高性能 Bukkit/Spigot 分支。
        /// </summary>
        Paper,

        /// <summary>
        /// Folia 服务器，Paper 的多线程分支。
        /// </summary>
        Folia,

        /// <summary>
        /// Velocity 代理服务器，高性能反向代理。
        /// </summary>
        Velocity,

        /// <summary>
        /// Purpur 服务器，Paper 的扩展分支，提供更多配置选项。
        /// </summary>
        Purpur,

        /// <summary>
        /// Leaves 服务器，Paper 的分支，专注于性能优化和原版兼容。
        /// </summary>
        Leaves,

        /// <summary>
        /// Leaf 服务器，另一个 Paper 分支，提供额外的修复和优化。
        /// </summary>
        Leaf
    }
}
