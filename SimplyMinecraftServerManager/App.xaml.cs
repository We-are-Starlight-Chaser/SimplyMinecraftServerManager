// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Extensions;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.ViewModels.Pages;
using SimplyMinecraftServerManager.ViewModels.Windows;
using SimplyMinecraftServerManager.Views.Pages;
using SimplyMinecraftServerManager.Views.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace SimplyMinecraftServerManager
{
    /// <summary>
    /// 应用程序入口点，负责 DI 容器初始化、生命周期管理和日志记录。
    /// </summary>
    public partial class App
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "smsm", "logs", $"app_{DateTime.Now:yyyyMMdd}.log");
        private static readonly string LogDateFormat = "yyyy-MM-dd HH:mm:ss.fff";

        private static readonly IHost _host = Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(c =>
            {
                var basePath = Path.GetDirectoryName(AppContext.BaseDirectory);
                if (!string.IsNullOrEmpty(basePath))
                    c.SetBasePath(basePath);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddNavigationViewPageProvider();

                services.AddHostedService<ApplicationHostService>();

                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<ITaskBarService, TaskBarService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IContentDialogService, ContentDialogService>();
                services.AddSingleton<NavigationParameterService>();
                services.AddSingleton<AppNotificationService>();

                services.AddSingleton<FirstTimeUsingWindow>();
                services.AddSingleton<FirstTimeUsingWindowViewModel>();

                services.AddSingleton<INavigationWindow, MainWindow>();
                services.AddSingleton<MainWindowViewModel>(provider =>
                {
                    var downloadsViewModel = provider.GetRequiredService<DownloadsViewModel>();
                    var notificationService = provider.GetRequiredService<AppNotificationService>();
                    return new MainWindowViewModel(downloadsViewModel, notificationService);
                });

                services.AddSingleton<DashboardPage>();
                services.AddSingleton<DashboardViewModel>();

                services.AddSingleton<DownloadPage>();
                services.AddSingleton<DownloadViewModel>();

                services.AddSingleton<ServersPage>();
                services.AddSingleton<ServersViewModel>();

                services.AddSingleton<JdkPage>();
                services.AddSingleton<JdkViewModel>();

                services.AddSingleton<InstancePage>();
                services.AddSingleton<InstanceViewModel>();

                services.AddSingleton<DownloadsPage>();
                services.AddSingleton<DownloadsViewModel>();

                services.AddSingleton<ToolsPage>();
                services.AddSingleton<ToolsViewModel>();

                services.AddSingleton<SettingsPage>();
                services.AddSingleton<SettingsViewModel>();
            }).Build();

        /// <summary>获取应用程序的依赖注入服务提供者</summary>
        public static IServiceProvider Services
        {
            get { return _host.Services; }
        }

    private async void OnStartup(object sender, StartupEventArgs e)
        {
            try
            {
                EnsureLogDirectory();
                Log("Application starting...");
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                var version = Environment.OSVersion.Version;
                int major = version.Major;
                int minor = version.Minor;
                bool isUnsupportedSystem = !OperatingSystem.IsWindowsVersionAtLeast(10);
                if (isUnsupportedSystem)
                {
                    MessageBox.Show("本程序不支持旧版的TLS协议，请升级系统或安装补丁！", "SMSM");
                }

                await Task.Run(() => ConfigManager.Load());
                
                SetToShowFirstTimeUsingWindow();

                await _host.StartAsync();
                
                await Task.Run(() => InstanceManager.Load());

                var extLogger = new ExtensionLogger("host");
                _extensionLoader = new ExtensionLoader(extLogger);
                await _extensionLoader.LoadAllAsync();
                
                await Task.Run(() => PreloadNonCriticalData());

                int zstdThreads = Math.Max(1, Environment.ProcessorCount / 2);
                Environment.SetEnvironmentVariable("ZSTD_NBTHREADS", zstdThreads.ToString());

                Log("Application started successfully");
            }
            catch (Exception ex)
            {
                Log($"Startup error: {ex}");
                MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        private static void PreloadNonCriticalData()
        {
            try
            {
                var config = ConfigManager.Current;
            }
            catch (Exception ex)
            {
                Log($"Failed to preload non-critical data: {ex.Message}");
            }
        }

        private async void OnExit(object sender, ExitEventArgs e)
        {
            try
            {
                Log("Application shutting down...");
                if (_extensionLoader != null)
                {
                    await _extensionLoader.UnloadAllAsync();
                    _extensionLoader.Dispose();
                }
                ServerProcessManager.KillAll();
                InstanceManager.Shutdown();
                await _host.StopAsync();
                _host.Dispose();
                Log("Application shutdown complete");
            }
            catch (Exception ex)
            {
                Log($"Shutdown error: {ex}");
            }
            finally
            {
                lock (_logLock)
                {
                    _logWriter?.Dispose();
                    _logWriter = null;
                }
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log($"Dispatcher unhandled exception: {e.Exception}");
            MessageBox.Show($"发生错误: {e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Log($"Unhandled exception (IsTerminating={e.IsTerminating}): {ex}");
            if (e.IsTerminating)
            {
                Environment.Exit(1);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        }

        private static void EnsureLogDirectory()
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        private static readonly Lock _logLock = new();
        private static StreamWriter? _logWriter;
        private static ExtensionLoader? _extensionLoader;

        private static void Log(string message)
        {
            try
            {
                var writer = _logWriter;
                if (writer == null)
                {
                    lock (_logLock)
                    {
                        writer = _logWriter;
                        if (writer == null)
                        {
                            EnsureLogDirectory();
                            writer = new System.IO.StreamWriter(LogPath, append: true) { AutoFlush = true };
                            _logWriter = writer;
                        }
                    }
                }
                writer.Write('[');
                writer.Write(DateTime.Now.ToString(LogDateFormat));
                writer.Write("] ");
                writer.WriteLine(message);
                writer.Flush();
            }
            catch
            {
            }
        }
        [Conditional("DEBUG")]
        private static void SetToShowFirstTimeUsingWindow()
        {
            ConfigManager.Current.IsFirstTimeUsing = true;
        }
    }
}
