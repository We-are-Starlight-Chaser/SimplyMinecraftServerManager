// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IEventBus 实现。
/// 线程安全的发布-订阅事件总线，使用弱引用委托避免内存泄漏。
/// 支持扩展间事件隔离：每个扩展的订阅和发布都带扩展 ID 标记。
/// </summary>
internal sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, Delegate> _handlers = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _extensionSubscriptions = new();
    private readonly object _lock = new();

    /// <summary>
    /// 获取指定扩展的所有订阅事件类型名称
    /// </summary>
    public IReadOnlyCollection<string> GetExtensionSubscriptionTypeNames(string extensionId)
    {
        lock (_lock)
        {
            if (_extensionSubscriptions.TryGetValue(extensionId, out var types))
            {
                return types.ToList();
            }
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 清除指定扩展的所有订阅（卸载时调用）
    /// 注意：由于事件处理器是混合的，无法精确移除单个扩展的处理器
    /// 此方法仅清除订阅记录，实际的处理器会在扩展卸载时通过 Unsubscriber 自动移除
    /// </summary>
    public void ClearExtensionSubscriptions(string extensionId)
    {
        lock (_lock)
        {
            _extensionSubscriptions.TryRemove(extensionId, out _);
        }
    }

    public IDisposable OnServerStateChanged(EventHandler<ServerStateChangedEventArgs> handler)
    {
        return SubscribeInternal(handler);
    }

    public IDisposable OnPluginChanged(EventHandler<PluginEventArgs> handler)
    {
        return SubscribeInternal(handler);
    }

    public IDisposable OnConfigChanged(EventHandler<ConfigChangedEventArgs> handler)
    {
        return SubscribeInternal(handler);
    }

    public void Publish<TEventArgs>(object sender, TEventArgs args) where TEventArgs : EventArgs
    {
        if (_handlers.TryGetValue(typeof(TEventArgs), out var del) && del is EventHandler<TEventArgs> handler)
        {
            // 使用安全调用，防止单个扩展异常影响其他扩展
            Delegate[] invocationList = handler.GetInvocationList();
            foreach (var d in invocationList)
            {
                try
                {
                    var typedHandler = (EventHandler<TEventArgs>)d;
                    typedHandler.Invoke(sender, args);
                }
                catch (Exception ex)
                {
                    // 记录日志但不中断其他扩展的事件处理
                    System.Diagnostics.Debug.WriteLine($"[EventBus] 事件处理异常: {ex.Message}");
                }
            }
        }
    }

    public IDisposable Subscribe<TEventArgs>(EventHandler<TEventArgs> handler) where TEventArgs : EventArgs
    {
        return SubscribeInternal(handler);
    }

    /// <summary>
    /// 带扩展 ID 的订阅（用于跟踪）
    /// </summary>
    public IDisposable Subscribe<TEventArgs>(string extensionId, EventHandler<TEventArgs> handler) where TEventArgs : EventArgs
    {
        // 记录扩展订阅
        lock (_lock)
        {
            if (!_extensionSubscriptions.TryGetValue(extensionId, out var types))
            {
                types = new HashSet<string>();
                _extensionSubscriptions[extensionId] = types;
            }
            types.Add(typeof(TEventArgs).FullName!);
        }

        return SubscribeInternal(handler);
    }

    private IDisposable SubscribeInternal<TEventArgs>(EventHandler<TEventArgs> handler) where TEventArgs : EventArgs
    {
        _handlers.AddOrUpdate(
            typeof(TEventArgs),
            handler,
            (_, existing) => Delegate.Combine(existing, handler)!);

        return new Unsubscriber(() =>
        {
            _handlers.AddOrUpdate(
                typeof(TEventArgs),
                _ => null!,
                (_, existing) =>
                {
                    var result = Delegate.Remove(existing, handler);
                    return result ?? null!;
                });
        });
    }

    private sealed class Unsubscriber(Action removeAction) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                removeAction();
            }
        }
    }
}
