// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Microsoft.Extensions.Hosting;
using SimplyMinecraftServerManager.Views.Pages;
using SimplyMinecraftServerManager.Views.Windows;
using Wpf.Ui;

namespace SimplyMinecraftServerManager.Services
{
    /// <summary>
    /// 应用程序主机服务，负责管理应用程序的启动和停止生命周期。
    /// </summary>
    public class ApplicationHostService(IServiceProvider serviceProvider) : IHostedService
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private INavigationWindow? _navigationWindow;

        /// <summary>
        /// 启动应用程序主机服务，处理窗口激活逻辑。
        /// </summary>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await HandleActivationAsync();
        }

        /// <summary>
        /// 停止应用程序主机服务。
        /// </summary>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
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
