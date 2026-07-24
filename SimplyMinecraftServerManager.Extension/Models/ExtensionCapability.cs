// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 描述扩展所需能力的标志枚举。
/// 宿主根据此标志决定是否加载扩展或向其提供对应服务。
/// </summary>
[Flags]
public enum ExtensionCapability
{
    /// <summary>扩展无需特殊能力（默认值）</summary>
    None = 0,

    /// <summary>扩展需要访问服务器实例列表</summary>
    InstanceAccess = 1 << 0,

    /// <summary>扩展需要控制服务器进程（启动/停止/发送命令）</summary>
    ServerControl = 1 << 1,

    /// <summary>扩展需要下载文件</summary>
    Download = 1 << 2,

    /// <summary>扩展需要订阅事件</summary>
    EventSubscription = 1 << 3,

    /// <summary>扩展需要注册自定义导航项</summary>
    Navigation = 1 << 4,

    /// <summary>扩展需要注册自定义设置面板</summary>
    SettingsPanel = 1 << 5,

    /// <summary>扩展需要执行进程（启动外部程序）</summary>
    ProcessExecution = 1 << 6,

    /// <summary>扩展需要使用 P/Invoke 调用非托管代码</summary>
    PInvoke = 1 << 7,

    /// <summary>扩展需要发起网络请求</summary>
    Network = 1 << 8,

    /// <summary>扩展需要查询自身安全状态</summary>
    SecurityAudit = 1 << 9,

    /// <summary>扩展需要注册定时/延迟任务</summary>
    Scheduling = 1 << 10,

    /// <summary>扩展需要显示通知给用户</summary>
    Notification = 1 << 11,

    /// <summary>扩展需要跨扩展通信</summary>
    InterExtension = 1 << 12,
}
