// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 扩展生命周期状态。
/// </summary>
public enum ExtensionState
{
    /// <summary>未加载</summary>
    NotLoaded,

    /// <summary>正在加载程序集</summary>
    Loading,

    /// <summary>正在初始化（InitAsync 已调用，等待完成）</summary>
    Initializing,

    /// <summary>初始化完成，CanExecute 已通过，等待执行</summary>
    Ready,

    /// <summary>正在执行（ExecuteAsync 已调用）</summary>
    Running,

    /// <summary>正在卸载/释放资源</summary>
    Disposing,

    /// <summary>已卸载</summary>
    Disposed,

    /// <summary>加载失败或运行时出错</summary>
    Faulted
}
