// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 文件访问级别。
/// </summary>
[Flags]
public enum FileAccessLevel
{
    /// <summary>无权限</summary>
    None = 0,

    /// <summary>读取</summary>
    Read = 1 << 0,

    /// <summary>写入（创建/修改）</summary>
    Write = 1 << 1,

    /// <summary>删除</summary>
    Delete = 1 << 2,

    /// <summary>执行（仅限可执行文件）</summary>
    Execute = 1 << 3,

    /// <summary>完整读写</summary>
    ReadWrite = Read | Write,

    /// <summary>完全控制</summary>
    Full = Read | Write | Delete | Execute
}
