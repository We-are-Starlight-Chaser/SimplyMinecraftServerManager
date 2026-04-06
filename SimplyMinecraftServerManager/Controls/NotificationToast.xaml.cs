using Microsoft.Extensions.DependencyInjection;
using SimplyMinecraftServerManager.Models;
using SimplyMinecraftServerManager.Services;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace SimplyMinecraftServerManager.Controls
{
    public partial class NotificationToast : UserControl
    {
        private bool _isClosing;
        private CancellationTokenSource? _dismissCts;

        public NotificationToast()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BeginEnterAnimation();
            StartAutoDismiss();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _dismissCts?.Cancel();
            _dismissCts?.Dispose();
            _dismissCts = null;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _ = CloseAsync();
        }

        private void BeginEnterAnimation()
        {
            var storyboard = new Storyboard();

            var opacityAnimation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opacityAnimation, Root);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));

            var translateAnimation = new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translateAnimation, Root);
            Storyboard.SetTargetProperty(translateAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(translateAnimation);
            storyboard.Begin();
        }

        private void StartAutoDismiss()
        {
            _dismissCts?.Cancel();
            _dismissCts?.Dispose();
            _dismissCts = new CancellationTokenSource();

            if (DataContext is not AppNotificationItem item)
            {
                return;
            }

            _ = AutoDismissAsync(item.Duration, _dismissCts.Token);
        }

        private async Task AutoDismissAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(duration, cancellationToken);
                await Dispatcher.InvokeAsync(CloseAsync);
            }
            catch (TaskCanceledException)
            {
            }
        }

        private async Task CloseAsync()
        {
            if (_isClosing || DataContext is not AppNotificationItem item)
            {
                return;
            }

            _isClosing = true;
            _dismissCts?.Cancel();

            var completionSource = new TaskCompletionSource();
            var storyboard = new Storyboard();

            var opacityAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(opacityAnimation, Root);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));

            var translateAnimation = new DoubleAnimation(0, 24, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(translateAnimation, Root);
            Storyboard.SetTargetProperty(translateAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(translateAnimation);
            storyboard.Completed += (_, _) => completionSource.TrySetResult();
            storyboard.Begin();

            await completionSource.Task;
            App.Services.GetRequiredService<AppNotificationService>().Remove(item.Id);
        }
    }
}
