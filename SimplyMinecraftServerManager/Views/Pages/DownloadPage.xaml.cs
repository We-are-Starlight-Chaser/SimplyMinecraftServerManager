using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class DownloadPage : INavigableView<DownloadViewModel>, IDisposable
    {
        public DownloadViewModel ViewModel { get; }

        private DispatcherTimer? _scrollDebounceTimer;
        private const int SCROLL_DEBOUNCE_MS = 300; // 300毫秒防抖

        public DownloadPage(DownloadViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
            
            // 初始化防抖计时器
            _scrollDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SCROLL_DEBOUNCE_MS),
                IsEnabled = false
            };
            _scrollDebounceTimer.Tick += OnScrollDebounceTimerTick;
        }

        private void OnPluginSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.SearchPluginsCommand.Execute(null);
            }
        }

        private void OnServerVersionsScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;

            // 检查是否滚动到底部
            if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 100 && 
                scrollViewer.ScrollableHeight > 0)
            {
                // 重置并启动防抖计时器
                _scrollDebounceTimer?.Stop();
                _scrollDebounceTimer?.Start();
            }
            else
            {
                // 如果不是滚动到底部，停止计时器
                _scrollDebounceTimer?.Stop();
            }
        }

        private void OnScrollDebounceTimerTick(object? sender, EventArgs e)
        {
            // 停止计时器
            _scrollDebounceTimer?.Stop();
            
            // 检查是否仍有更多版本可加载且不在加载中
            if (ViewModel.HasMoreVersions && !ViewModel.IsLoadingMore)
            {
                ViewModel.LoadMoreVersionsCommand.Execute(null);
            }
        }

        public void Dispose()
        {
            _scrollDebounceTimer?.Stop();
            _scrollDebounceTimer?.Tick -= OnScrollDebounceTimerTick;
            _scrollDebounceTimer = null;
            GC.SuppressFinalize(this);
        }
    }
}
