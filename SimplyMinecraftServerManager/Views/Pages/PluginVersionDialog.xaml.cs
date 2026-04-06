using SimplyMinecraftServerManager.ViewModels.Dialogs;
using System.Windows;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class PluginVersionDialog : ContentDialog
    {
        public PluginVersionDialog()
        {
            InitializeComponent();
        }

        public bool IsConfirmed { get; private set; }

        public PluginVersionListItem? SelectedVersionItem =>
            (DataContext as PluginVersionDialogViewModel)?.SelectedVersionItem;

        private void OnPrimaryButtonClick(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            Hide();
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Hide();
        }
    }
}
