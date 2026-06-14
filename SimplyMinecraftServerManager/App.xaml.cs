using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.ViewModels.Pages;
using SimplyMinecraftServerManager.ViewModels.Windows;
using SimplyMinecraftServerManager.Views.Pages;
using SimplyMinecraftServerManager.Views.Windows;
using System.IO;
using System.Net;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace SimplyMinecraftServerManager
{
    public partial class App
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "smsm", "logs", $"app_{DateTime.Now:yyyyMMdd}.log");

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
                bool isTargetSystem = !OperatingSystem.IsWindowsVersionAtLeast(10);//(major == 6 && minor >= 0 && minor <= 3);
                if (isTargetSystem)
                {
                    MessageBox.Show("本程序不支持旧版的TLS协议，请升级系统或安装补丁！", "SMSM");
                }

                await Task.Run(() => ConfigManager.Load());
                
                await _host.StartAsync();
                
                await Task.Run(() => InstanceManager.Load());
                
                await Task.Run(() => PreloadNonCriticalData());
                
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
            catch { }
        }

        private async void OnExit(object sender, ExitEventArgs e)
        {
            try
            {
                Log("Application shutting down...");
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

        private static readonly object _logLock = new();
        private static System.IO.StreamWriter? _logWriter;

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
                            writer = new System.IO.StreamWriter(LogPath, append: true) { AutoFlush = false };
                            _logWriter = writer;
                        }
                    }
                }
                writer.Write('[');
                writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                writer.Write("] ");
                writer.Write(message);
                writer.WriteLine();
                writer.Flush();
            }
            catch
            {
            }
        }
    }
}
