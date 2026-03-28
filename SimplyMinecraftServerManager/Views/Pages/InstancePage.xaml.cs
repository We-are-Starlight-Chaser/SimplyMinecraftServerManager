using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class InstancePage : INavigableView<InstanceViewModel>
    {
        ScrollViewer? _sv;
        public InstanceViewModel ViewModel { get; }

        public InstancePage(InstanceViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            // 订阅控制台事件
            ViewModel.ConsoleLineAdded += OnConsoleLineAdded;
            ViewModel.ConsoleCleared += OnConsoleCleared;

            // 页面加载完成后初始化控制台
            Loaded += OnPageLoaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            // 初始化 FlowDocument 内容并获取 ScrollViewer
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _sv = ConsoleScrollViewer.Template.FindName("PART_ContentHost", ConsoleScrollViewer) as ScrollViewer;
            }));
            InitializeConsole();
        }

        private async void InitializeConsole()
        {
            if (ConsoleParagraph == null) return;

            var text = ViewModel.GetConsoleText();
            if (!string.IsNullOrEmpty(text))
            {
                ConsoleParagraph.Inlines.Clear();
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        ConsoleParagraph.Inlines.Add(new Run(line));
                        ConsoleParagraph.Inlines.Add(new LineBreak());
                    }
                }

                // 检查是否需要显示空提示
                UpdateEmptyHintVisibility(lines.Length > 0);
            }
            else
            {
                // 没有内容时显示空提示
                UpdateEmptyHintVisibility(false);
            }

            // 如果启用了自动滚动，滚动到底部
            if (ViewModel.AutoScroll && _sv != null)
            {
                _sv.ScrollToBottom();
            }
        }

        private void UpdateEmptyHintVisibility(bool hasContent)
        {
            ConsoleEmptyHint?.Visibility = hasContent || ViewModel.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnConsoleLineAdded(object? sender, string line)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (ConsoleParagraph == null) return;

                // 检查是否需要显示空提示
                UpdateEmptyHintVisibility(true);

                // 添加新行
                ConsoleParagraph.Inlines.Add(new Run(line));
                ConsoleParagraph.Inlines.Add(new LineBreak());

                // 限制行数
                var inlines = new List<Inline>();
                var current = ConsoleParagraph.Inlines.FirstInline;
                while (current != null)
                {
                    inlines.Add(current);
                    current = current.NextInline;
                }

                if (inlines.Count > InstanceViewModel.MaxConsoleLines * 2)
                {
                    // 移除前面的元素以限制行数
                    int removeCount = inlines.Count - InstanceViewModel.MaxConsoleLines;
                    for (int i = 0; i < removeCount && i < inlines.Count; i++)
                    {
                        ConsoleParagraph.Inlines.Remove(inlines[i]);
                    }
                }

                // 自动滚动 - 将滚动条移动到底部
                if (ViewModel.AutoScroll && _sv != null)
                {
                    _sv.ScrollToBottom();
                }
                
            });
        }

        private void OnConsoleCleared(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ConsoleParagraph?.Inlines.Clear();
                // 检查是否需要显示空提示
                UpdateEmptyHintVisibility(false);
            });
        }

        private void OnCommandKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.SendCommandCommand.Execute(null);
            }
        }

        /// <summary>
        /// 设置要加载的实例 ID（在导航前调用）
        /// </summary>
        public void SetInstanceId(string instanceId)
        {
            ViewModel.LoadInstance(instanceId);
            // 重新初始化控制台
            Dispatcher.BeginInvoke(() => InitializeConsole(), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// TabControl 选中项改变时触发
        /// </summary>
        private void OnTabSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 检查是否是仪表盘选项卡被选中
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TabItem tabItem && tabItem == DashboardTabItem)
            {
                // 每次进入仪表盘选项卡时刷新数据
                ViewModel.RefreshDashboardCommand.Execute(null);
            }
        }
    }
}