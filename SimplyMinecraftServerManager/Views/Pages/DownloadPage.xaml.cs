using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows.Input;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class DownloadPage : INavigableView<DownloadViewModel>
    {
        public DownloadViewModel ViewModel { get; }

        public DownloadPage(DownloadViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }

        private void OnPluginSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.SearchPluginsCommand.Execute(null);
            }
        }
    }
}
