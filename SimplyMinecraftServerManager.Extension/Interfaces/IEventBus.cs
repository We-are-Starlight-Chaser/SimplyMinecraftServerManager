// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 事件总线，供扩展订阅宿主和服务器事件。
/// </summary>
public interface IEventBus
{
    /// <summary>订阅服务器状态变更事件</summary>
    /// <param name="handler">事件处理器</param>
    /// <returns>用于取消订阅的 IDisposable</returns>
    IDisposable OnServerStateChanged(EventHandler<ServerStateChangedEventArgs> handler);

    /// <summary>订阅插件安装/卸载事件</summary>
    IDisposable OnPluginChanged(EventHandler<PluginEventArgs> handler);

    /// <summary>订阅服务器配置变更事件</summary>
    IDisposable OnConfigChanged(EventHandler<ConfigChangedEventArgs> handler);

    /// <summary>发布自定义事件到事件总线</summary>
    /// <typeparam name="TEventArgs">事件参数类型</typeparam>
    /// <param name="sender">事件发送者（通常传 this）</param>
    /// <param name="args">事件参数</param>
    void Publish<TEventArgs>(object sender, TEventArgs args) where TEventArgs : EventArgs;

    /// <summary>
    /// 订阅自定义事件。
    /// </summary>
    /// <typeparam name="TEventArgs">事件参数类型</typeparam>
    /// <param name="handler">事件处理器</param>
    /// <returns>用于取消订阅的 IDisposable</returns>
    IDisposable Subscribe<TEventArgs>(EventHandler<TEventArgs> handler) where TEventArgs : EventArgs;
}
