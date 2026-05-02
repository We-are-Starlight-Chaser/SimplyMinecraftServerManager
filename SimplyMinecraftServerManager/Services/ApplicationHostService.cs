using Microsoft.Extensions.Hosting;
using SimplyMinecraftServerManager.Views.Pages;
using SimplyMinecraftServerManager.Views.Windows;
using Wpf.Ui;

namespace SimplyMinecraftServerManager.Services
{
    public class ApplicationHostService(IServiceProvider serviceProvider) : IHostedService
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private INavigationWindow? _navigationWindow;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await HandleActivationAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        private async Task HandleActivationAsync()
        {
            if (!Application.Current.Windows.OfType<MainWindow>().Any())
            {
                _navigationWindow = (
                    _serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow
                )!;
                _navigationWindow?.ShowWindow();

                _navigationWindow?.Navigate(typeof(DashboardPage));
            }

            await Task.CompletedTask;
        }
    }
}
