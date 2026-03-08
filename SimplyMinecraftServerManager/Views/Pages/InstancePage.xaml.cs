using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class InstancePage : INavigableView<InstanceViewModel>
    {
        ScrollViewer sv;
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
            // 初始化 FlowDocument 内容
            InitializeConsole();
            ConsoleScrollViewer.Dispatcher.BeginInvoke(new Action(() =>
            {
                sv = ConsoleScrollViewer.Template.FindName("PART_ContentHost",ConsoleScrollViewer) as ScrollViewer;
            }));
        }

        private void InitializeConsole()
        {
            if (ConsoleParagraph == null) return;

            var text = ViewModel.GetConsoleText();
            if (!string.IsNullOrEmpty(text))
            {
                ConsoleParagraph.Inlines.Clear();
                var lines = text.Split('\n');
                foreach (var line in lines)
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        ConsoleParagraph.Inlines.Add(new Run(line));
                        ConsoleParagraph.Inlines.Add(new LineBreak());
                    }
                }
            }
        }

        private void OnConsoleLineAdded(object? sender, string line)
        {
            Dispatcher.Invoke(() =>
            {
                if (ConsoleParagraph == null) return;

                // 添加新行
                ConsoleParagraph.Inlines.Add(new Run(line));
                ConsoleParagraph.Inlines.Add(new LineBreak());

                // 限制行数
                while (ConsoleParagraph.Inlines.Count > InstanceViewModel.MaxConsoleLines * 2)
                {
                    // 移除前两个元素（Run + LineBreak）
                    if (ConsoleParagraph.Inlines.FirstInline != null)
                    {
                        ConsoleParagraph.Inlines.Remove(ConsoleParagraph.Inlines.FirstInline);
                    }
                    if (ConsoleParagraph.Inlines.FirstInline != null)
                    {
                        ConsoleParagraph.Inlines.Remove(ConsoleParagraph.Inlines.FirstInline);
                    }
                }

                // 自动滚动 - 将滚动条移动到底部
                if (ViewModel.AutoScroll && ConsoleScrollViewer != null)
                {
                    sv.ScrollToBottom();
                }
            });
        }

        private void OnConsoleCleared(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ConsoleParagraph?.Inlines.Clear();
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
        /// 在可视树中查找指定类型的子元素
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        /// <summary>
        /// TabControl 选中项改变时触发
        /// </summary>
        private void OnTabSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 检查是否是性能选项卡被选中
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TabItem tabItem && tabItem == PerformanceTabItem)
            {
                // 每次进入性能选项卡时刷新存储空间统计
                ViewModel.RefreshStorageInfoCommand.Execute(null);
            }
        }
    }
}