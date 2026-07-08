// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.ViewModels.Dialogs;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class ServerPingerDialog : ContentDialog
    {
        public ServerPingerDialogViewModel ViewModel { get;}
        public ServerPingerDialog(ServerPingerDialogViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}
