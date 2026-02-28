using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject,INavigationAware
    {
        [ObservableProperty]
        private string _userName;
        [ObservableProperty]
        private string? _news;
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
    }
}
