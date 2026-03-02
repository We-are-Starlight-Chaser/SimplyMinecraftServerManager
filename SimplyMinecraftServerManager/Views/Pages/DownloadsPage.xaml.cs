using SimplyMinecraftServerManager.ViewModels.Pages;
using SimplyMinecraftServerManager.ViewModels.Windows;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class DownloadsPage : INavigableView<DownloadsViewModel>
    {
        public DownloadsViewModel ViewModel { get; }

        public DownloadsPage(DownloadsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();

            // 订阅任务数变化事件，更新主窗口角标
            ViewModel.TaskCountChanged += OnTaskCountChanged;
        }

        private void OnTaskCountChanged(object? sender, int count)
        {
            // 获取主窗口 ViewModel 并更新角标
            if (Application.Current.MainWindow is Views.Windows.MainWindow mainWindow)
            {
                mainWindow.ViewModel.UpdateDownloadTaskBadge(count);
            }
        }
    }
}
