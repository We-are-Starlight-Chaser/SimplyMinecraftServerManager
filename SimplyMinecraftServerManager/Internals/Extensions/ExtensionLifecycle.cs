// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展生命周期状态机，管理单个扩展从加载到卸载的完整状态转换。
/// 线程安全：所有状态变更通过 lock 保护。
/// </summary>
internal sealed class ExtensionLifecycle
{
    private readonly object _lock = new();
    private ExtensionState _state = ExtensionState.NotLoaded;
    private DateTime? _loadedAt;
    private DateTime? _disposedAt;
    private Exception? _fault;

    /// <summary>当前状态</summary>
    public ExtensionState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>加载时间</summary>
    public DateTime? LoadedAt
    {
        get { lock (_lock) return _loadedAt; }
    }

    /// <summary>卸载时间</summary>
    public DateTime? DisposedAt
    {
        get { lock (_lock) return _disposedAt; }
    }

    /// <summary>故障异常（仅 Faulted 状态有效）</summary>
    public Exception? Fault
    {
        get { lock (_lock) return _fault; }
    }

    /// <summary>尝试转换到目标状态，返回是否成功</summary>
    public bool TryTransitionTo(ExtensionState target, Exception? fault = null)
    {
        lock (_lock)
        {
            if (!CanTransitionTo(target))
            {
                return false;
            }

            _state = target;

            if (target == ExtensionState.Loading)
            {
                _loadedAt = DateTime.UtcNow;
            }
            else if (target == ExtensionState.Disposed)
            {
                _disposedAt = DateTime.UtcNow;
            }
            else if (target == ExtensionState.Faulted)
            {
                _fault = fault;
            }

            return true;
        }
    }

    /// <summary>强制设置为 Faulted 状态（用于异常恢复）</summary>
    public void SetFaulted(Exception exception)
    {
        lock (_lock)
        {
            _state = ExtensionState.Faulted;
            _fault = exception;
        }
    }

    private bool CanTransitionTo(ExtensionState target)
    {
        return _state switch
        {
            ExtensionState.NotLoaded => target == ExtensionState.Loading,
            ExtensionState.Loading => target is ExtensionState.Initializing or ExtensionState.Faulted,
            ExtensionState.Initializing => target is ExtensionState.Ready or ExtensionState.Faulted or ExtensionState.Disposing,
            ExtensionState.Ready => target is ExtensionState.Running or ExtensionState.Disposing,
            ExtensionState.Running => target is ExtensionState.Disposing or ExtensionState.Faulted,
            ExtensionState.Disposing => target is ExtensionState.Disposed or ExtensionState.Faulted,
            ExtensionState.Disposed => false, // 终态
            ExtensionState.Faulted => target == ExtensionState.Disposing, // 允许从 Faulted 清理
            _ => false
        };
    }
}
