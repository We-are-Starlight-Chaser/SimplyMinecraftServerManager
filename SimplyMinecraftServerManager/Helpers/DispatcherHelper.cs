using System.Windows.Threading;

namespace SimplyMinecraftServerManager.Helpers
{
    internal static class DispatcherHelper
    {
        private static Dispatcher? _dispatcher;

        public static void Initialize() => _dispatcher = Application.Current?.Dispatcher;

        public static Dispatcher? Dispatcher => _dispatcher ??= Application.Current?.Dispatcher;

        public static bool CheckAccess() => _dispatcher?.CheckAccess() ?? true;

        public static void InvokeIfNeeded(Action action)
        {
            var d = _dispatcher;
            if (d == null || d.CheckAccess())
            {
                action();
                return;
            }
            d.BeginInvoke(action, DispatcherPriority.Background);
        }

        public static void InvokeIfNeededSync(Action action)
        {
            var d = _dispatcher;
            if (d == null || d.CheckAccess())
            {
                action();
                return;
            }
            d.Invoke(action, DispatcherPriority.Background);
        }
    }

    internal class ThrottledDispatcher(DispatcherPriority priority = DispatcherPriority.Background, int intervalMs = 50) : IDisposable
    {
        private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("No dispatcher");
        private readonly DispatcherPriority _priority = priority;
        private readonly int _intervalMs = intervalMs;
        private readonly Lock _lock = new();
        private readonly Queue<Action> _pendingActions = new();
        private Timer? _timer;
        private volatile bool _isPending;

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
                actions = new List<Action>(_pendingActions);
                while (_pendingActions.TryDequeue(out var a)) { }
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

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}