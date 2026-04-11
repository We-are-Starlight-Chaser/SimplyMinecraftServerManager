using System.Windows;
using System.Windows.Threading;

namespace SimplyMinecraftServerManager.Helpers
{
    internal static class DispatcherHelper
    {
        private static readonly Dispatcher _dispatcher = Application.Current?.Dispatcher;

        public static Dispatcher? Dispatcher => _dispatcher;

        public static bool CheckAccess() => _dispatcher?.CheckAccess() ?? true;

        public static void InvokeIfNeeded(Action action)
        {
            if (_dispatcher == null || _dispatcher.CheckAccess())
            {
                action();
                return;
            }
            _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }

        public static void InvokeIfNeededSync(Action action)
        {
            if (_dispatcher == null || _dispatcher.CheckAccess())
            {
                action();
                return;
            }
            _dispatcher.Invoke(action, DispatcherPriority.Background);
        }
    }

    internal class ThrottledDispatcher
    {
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherPriority _priority;
        private readonly int _intervalMs;
        private DateTime _lastInvoke = DateTime.MinValue;
        private readonly Queue<Action> _pendingActions = new();
        private readonly object _lock = new();
        private Timer? _timer;
        private bool _isPending;

        public ThrottledDispatcher(DispatcherPriority priority = DispatcherPriority.Background, int intervalMs = 16)
        {
            _dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("No dispatcher");
            _priority = priority;
            _intervalMs = intervalMs;
        }

        public void Invoke(Action action)
        {
            lock (_lock)
            {
                _pendingActions.Enqueue(action);
                if (_isPending) return;
                _isPending = true;
            }

            var now = DateTime.Now;
            var delay = _intervalMs - (int)(now - _lastInvoke).TotalMilliseconds;
            delay = Math.Max(1, delay);

            _timer?.Dispose();
            _timer = new Timer(OnTimerTick, null, delay, Timeout.Infinite);
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
                actions = new List<Action>(_pendingActions);
                _pendingActions.Clear();
                _isPending = false;
            }

            _lastInvoke = DateTime.Now;
            _dispatcher.BeginInvoke(() =>
            {
                foreach (var action in actions)
                {
                    try { action(); } catch { }
                }
            }, _priority);
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}