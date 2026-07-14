// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 定义扩展的核心契约。
/// </summary>
public interface IExtension : IDisposable
{
    IExtensionMetadata Metadata { get; }

    /// <summary>
    /// 扩展所需的能力标志。
    /// 宿主据此决定是否加载扩展或向其提供对应服务。
    /// </summary>
    ExtensionCapability RequiredCapabilities => ExtensionCapability.None;

    /// <summary>
    /// 检查当前环境是否满足执行条件。
    /// 在 ExecuteAsync 之前调用。
    /// </summary>
    bool CanExecute();

    /// <summary>
    /// 初始化扩展（注册事件、加载配置等）。
    /// 在所有依赖初始化完成后、CanExecute 之前调用。
    /// </summary>
    Task InitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行扩展的主要逻辑。
    /// 仅在 CanExecute 返回 true 后调用。
    /// </summary>
    Task ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 宿主在向扩展注入上下文后调用。
    /// 扩展应在此处保存 IExtensionContext 引用。
    /// </summary>
    /// <param name="context">扩展上下文</param>
    void SetContext(IExtensionContext context) { }

    /// <summary>服务器启动后的回调</summary>
    void OnServerStarted(string instanceId) { }

    /// <summary>服务器停止后的回调</summary>
    void OnServerStopped(string instanceId) { }

    /// <summary>插件安装后的回调</summary>
    void OnPluginInstalled(string instanceId, string pluginName) { }

    /// <summary>配置变更后的回调</summary>
    void OnConfigChanged(string instanceId, string key, string? value) { }

    /// <summary>宿主关闭时调用，用于释放资源</summary>
    void OnShutdown() { }
}
