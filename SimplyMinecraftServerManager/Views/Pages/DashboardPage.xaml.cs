using SimplyMinecraftServerManager.Controls;
using SimplyMinecraftServerManager.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class DashboardPage : INavigableView<DashboardViewModel>
    {
        public DashboardViewModel ViewModel { get; }

        public DashboardPage(DashboardViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
            AddServer();
        }
        private void AddServer()
        {
            grid_cards.Children.Add( new ServerCard("4U4N"));

        }
    }
}
