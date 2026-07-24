// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class InstancePage : INavigableView<InstanceViewModel>
    {
        public InstanceViewModel ViewModel { get; }

        public InstancePage(InstanceViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            // 每次导航进入时重置滚动位置
            ViewModel.NavigatedTo += (_, _) => ResetScrollPositions();
        }

        /// <summary>
        /// 重置所有 ScrollViewer 的滚动位置到顶部。
        /// </summary>
        internal void ResetScrollPositions()
        {
            ResetScrollPositionsRecursive(this);
        }

        private static void ResetScrollPositionsRecursive(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv)
                {
                    sv.ScrollToTop();
                }
                ResetScrollPositionsRecursive(child);
            }
        }

        /// <summary>
        /// 设置要加载的实例 ID（在导航前调用）
        /// </summary>
        public void SetInstanceId(string instanceId)
        {
            ViewModel.LoadInstance(instanceId);
        }

        /// <summary>
        /// TabControl 选中项改变时触发
        /// </summary>
        private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TabItem tabItem && tabItem == DashboardTabItem)
            {
                ViewModel.RefreshDashboardCommand.Execute(null);
            }

            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TabItem selectedTab && selectedTab == PlayersTabItem)
            {
                ViewModel.RefreshPlayersCommand.Execute(null);
            }
        }
    }
}
 
