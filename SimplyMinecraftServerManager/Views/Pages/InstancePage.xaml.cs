using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows.Controls;
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
 
