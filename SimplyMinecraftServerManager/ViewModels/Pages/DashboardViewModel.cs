using System.Diagnostics;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject
    {
        [RelayCommand]
        private void NavivateToSite(string uri)
        {
            Process.Start("explorer.exe", uri);
        }
    }
}
