using System.Diagnostics;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject,INavigationAware
    {
        [ObservableProperty]
        private string _userName;
        private bool _isInitialized = false;
        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();
            return Task.CompletedTask;
        }
        private void InitializeViewModel()
        {
            UserName = Environment.UserName;
            _isInitialized = true;
        }

        [RelayCommand]
        private void NavivateToSite(string uri)
        {
            Process.Start("explorer.exe", uri);
        }
    }
}
