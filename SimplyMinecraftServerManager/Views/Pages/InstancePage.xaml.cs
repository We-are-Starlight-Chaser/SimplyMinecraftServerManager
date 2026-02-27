using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows.Input;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class InstancePage : INavigableView<InstanceViewModel>
    {
        public InstanceViewModel ViewModel { get; }

        private string? _pendingInstanceId;

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
            _pendingInstanceId = instanceId;
            ViewModel.LoadInstance(instanceId);
        }

        private void OnCommandKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.SendCommandCommand.Execute(null);
            }
        }
    }
}
