using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private string _appVersion = String.Empty;

        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

        // AppConfig 配置项
        [ObservableProperty]
        private string _defaultJdkPath = "";

        [ObservableProperty]
        private string _language = "zh-CN";

        [ObservableProperty]
        private bool _autoAcceptEula = false;

        [ObservableProperty]
        private int _defaultMinMemoryMb = 1024;

        [ObservableProperty]
        private int _defaultMaxMemoryMb = 2048;

        [ObservableProperty]
        private int _downloadThreads = 4;

        [ObservableProperty]
        private int _preferredJdkDistributionIndex = 0;

        [ObservableProperty]
        private string _statusMessage = "";

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            CurrentTheme = ApplicationThemeManager.GetAppTheme();
            AppVersion = $"SMSM - {GetAssemblyVersion()}";

            // 加载 AppConfig
            LoadConfig();

            _isInitialized = true;
        }

        private void LoadConfig()
        {
            var config = ConfigManager.Current;
            DefaultJdkPath = config.DefaultJdkPath;
            Language = config.Language;
            AutoAcceptEula = config.AutoAcceptEula;
            DefaultMinMemoryMb = config.DefaultMinMemoryMb;
            DefaultMaxMemoryMb = config.DefaultMaxMemoryMb;
            DownloadThreads = config.DownloadThreads;
            PreferredJdkDistributionIndex = config.PreferredJdkDistribution == "Zulu" ? 1 : 0;
        }

        [RelayCommand]
        private void SaveConfig()
        {
            var config = ConfigManager.Current;
            config.DefaultJdkPath = DefaultJdkPath;
            config.Language = Language;
            config.AutoAcceptEula = AutoAcceptEula;
            config.DefaultMinMemoryMb = DefaultMinMemoryMb;
            config.DefaultMaxMemoryMb = DefaultMaxMemoryMb;
            config.DownloadThreads = DownloadThreads;
            config.PreferredJdkDistribution = PreferredJdkDistributionIndex == 0 ? "Adoptium" : "Zulu";

            ConfigManager.Save();
            StatusMessage = "设置已保存";

            // 更新下载管理器并发数
            DownloadManager.ReconfigureDefault(DownloadThreads);
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            var config = new AppConfig();
            DefaultJdkPath = config.DefaultJdkPath;
            Language = config.Language;
            AutoAcceptEula = config.AutoAcceptEula;
            DefaultMinMemoryMb = config.DefaultMinMemoryMb;
            DefaultMaxMemoryMb = config.DefaultMaxMemoryMb;
            DownloadThreads = config.DownloadThreads;
            PreferredJdkDistributionIndex = 0;
            StatusMessage = "已重置为默认值（需点击保存）";
        }

        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            switch (parameter)
            {
                case "theme_light":
                    if (CurrentTheme == ApplicationTheme.Light)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    CurrentTheme = ApplicationTheme.Light;

                    break;

                default:
                    if (CurrentTheme == ApplicationTheme.Dark)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    CurrentTheme = ApplicationTheme.Dark;

                    break;
            }
        }

        // 自动保存
        partial void OnDefaultJdkPathChanged(string value) => AutoSave();
        partial void OnLanguageChanged(string value) => AutoSave();
        partial void OnAutoAcceptEulaChanged(bool value) => AutoSave();
        partial void OnDefaultMinMemoryMbChanged(int value) => AutoSave();
        partial void OnDefaultMaxMemoryMbChanged(int value) => AutoSave();
        partial void OnDownloadThreadsChanged(int value) => AutoSave();
        partial void OnPreferredJdkDistributionIndexChanged(int value) => AutoSave();

        private void AutoSave()
        {
            if (!_isInitialized) return;
            SaveConfig();
        }
    }
}