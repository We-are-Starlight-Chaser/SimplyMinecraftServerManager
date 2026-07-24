// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Windows.Threading;

namespace SimplyMinecraftServerManager.Helpers
{
    /// <summary>
    /// WPF 调度器辅助类，提供线程安全的 UI 线程操作方法。
    /// </summary>
    internal static class DispatcherHelper
    {
        /// <summary>
        /// 如果需要则异步调用操作，确保操作在 UI 线程上执行。
        /// </summary>
        /// <param name="action">需要在 UI 线程上执行的操作。</param>
        public static void InvokeIfNeeded(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess())
            {
                action();
                return;
            }
            d.BeginInvoke(action, DispatcherPriority.Normal);
        }

        public static void InvokeIfNeededSync(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess())
            {
                action();
                return;
            }
            d.Invoke(action, DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// 节流调度器，将频繁的操作合并后在指定间隔内批量执行，避免 UI 线程被频繁调用阻塞。
    /// </summary>
    /// <param name="priority">调度器优先级，默认为 Background。</param>
    /// <param name="intervalMs">节流间隔（毫秒），默认为 50ms。</param>
    internal class ThrottledDispatcher(DispatcherPriority priority = DispatcherPriority.Background, int intervalMs = 50) : IDisposable
    {
        private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("No dispatcher");
        private readonly DispatcherPriority _priority = priority;
        private readonly int _intervalMs = intervalMs;
        private readonly Lock _lock = new();
        private readonly Queue<Action> _pendingActions = new();
        private Timer? _timer;
        private volatile bool _isPending;

        /// <summary>
        /// 将操作加入节流队列，在节流间隔后批量执行。
        /// </summary>
        /// <param name="action">要执行的操作。</param>
        public void Invoke(Action action)
        {
            lock (_lock)
            {
                _pendingActions.Enqueue(action);
                if (_isPending) return;
                _isPending = true;
            }

            if (_timer == null)
                _timer = new Timer(OnTimerTick, null, _intervalMs, Timeout.Infinite);
            else
                _timer.Change(_intervalMs, Timeout.Infinite);
        }

        private void OnTimerTick(object? state)
        {
            List<Action> actions;
            lock (_lock)
            {
                if (_pendingActions.Count == 0)
                {
                    _isPending = false;
                    return;
                }
                actions = [.. _pendingActions];
                _pendingActions.Clear();
                _isPending = false;
            }

            _dispatcher.BeginInvoke(() =>
            {
                foreach (var action in actions)
                {
                    try { action(); } catch { }
                }
            }, _priority);
        }

        /// <summary>
        /// 释放节流调度器的定时器资源。
        /// </summary>
        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}