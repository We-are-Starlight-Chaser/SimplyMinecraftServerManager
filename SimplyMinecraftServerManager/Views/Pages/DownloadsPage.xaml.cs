using SimplyMinecraftServerManager.ViewModels.Pages;
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
        }
    }
}
