// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class NewInstanceDialog : ContentDialog
    {
        public NewInstanceDialog()
        {
            InitializeComponent();
        }

        public async Task ShowDialogAsync()
        {
            await ShowAsync();
        }

        private async void OnPrimaryButtonClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewInstanceDialogViewModel vm)
            {
                await vm.CreateAsync();
            }
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewInstanceDialogViewModel vm)
            {
                vm.Cancel();
            }
            Hide();
        }
    }
}
