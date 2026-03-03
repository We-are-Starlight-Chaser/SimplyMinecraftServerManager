using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows.Input;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class ServersPage : INavigableView<ServersViewModel>
    {
        public ServersViewModel ViewModel { get; }

        public ServersPage(ServersViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }

        /// <summary>
        /// 点击卡片进入实例详情页
        /// </summary>
        private void OnCardClick(object sender, MouseButtonEventArgs e)
        {
            // 获取点击的 Border 元素
            if (sender is System.Windows.FrameworkElement element && element.DataContext is InstanceDisplayItem item)
            {
                // 导航到实例详情页
                ViewModel.ViewInstanceCommand.Execute(item);
            }
        }
    }
}
