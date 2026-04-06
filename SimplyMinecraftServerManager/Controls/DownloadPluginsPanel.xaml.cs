using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows.Controls;
using System.Windows.Input;

namespace SimplyMinecraftServerManager.Controls
{
    public partial class DownloadPluginsPanel : UserControl
    {
        public DownloadPluginsPanel()
        {
            InitializeComponent();
        }

        private void OnPluginSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is DownloadViewModel viewModel)
            {
                viewModel.SearchPluginsCommand.Execute(null);
            }
        }
    }
}
