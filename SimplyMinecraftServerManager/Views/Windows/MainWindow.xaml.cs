using SimplyMinecraftServerManager.ViewModels.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService,
            IContentDialogService contentDialogService
        )
        {
            ViewModel = viewModel;
            DataContext = this;

            SystemThemeWatcher.Watch(this);

            InitializeComponent();
            SetPageService(navigationViewPageProvider);

            navigationService.SetNavigationControl(RootNavigation);

            // 设置对话框宿主
            contentDialogService.SetDialogHost(RootContentDialogPresenter);

            // 订阅通知事件
            ViewModel.NotificationRequested += OnNotificationRequested;
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        /// <summary>
        /// 处理通知请求
        /// </summary>
        private void OnNotificationRequested(object? sender, string message)
        {
            // 在UI线程显示通知
            Dispatcher.Invoke(() =>
            {
                ShowNotificationSnackbar(message);
            });
        }

        /// <summary>
        /// 显示右下角通知Snackbar
        /// </summary>
        private void ShowNotificationSnackbar(string message)
        {
            var snackbar = new Snackbar(SnackbarPresenter)
            {
                Title = "任务完成",
                Content = message,
                Appearance = ControlAppearance.Success,
                Timeout = TimeSpan.FromSeconds(5), // 5秒后自动消失
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new System.Windows.Thickness(0, 0, 16, 16)
            };

            snackbar.Show();
        }

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            ViewModel.NotificationRequested -= OnNotificationRequested;
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            Application.Current.Shutdown();
        }

        INavigationView INavigationWindow.GetNavigation() => GetNavigation();

        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            if (serviceProvider.GetService(typeof(INavigationService)) is INavigationService navigationService)
            {
                navigationService.SetNavigationControl(RootNavigation);
            }
        }
    }
}
