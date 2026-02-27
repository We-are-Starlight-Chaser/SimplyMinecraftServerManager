using SimplyMinecraftServerManager.ViewModels.Pages;
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
    }
}
