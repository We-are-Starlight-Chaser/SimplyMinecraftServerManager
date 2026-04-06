using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;
        private bool _suppressAutoSave = false;
        private DispatcherTimer? _autoSaveTimer;

        [ObservableProperty]
        private string _appVersion = String.Empty;

        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

        [ObservableProperty]
        private string _language = "zh-CN";

        [ObservableProperty]
        private bool _autoAcceptEula = true;

        [ObservableProperty]
        private int _defaultMinMemoryMb = 1024;

        [ObservableProperty]
        private int _defaultMaxMemoryMb = 2048;

        [ObservableProperty]
        private int _downloadThreads = 4;

        [ObservableProperty]
        private bool _consoleWrapLines = false;

        [ObservableProperty]
        private string _consoleFontFamily = "Consolas";

        [ObservableProperty]
        private int _consoleFontSize = 12;

        [ObservableProperty]
        private ObservableCollection<string> _consoleFontFamilies = [];

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
            LoadConsoleFontFamilies();
            InitializeAutoSaveTimer();

            // 加载 AppConfig
            LoadConfig();

            _isInitialized = true;
        }

        private void LoadConfig()
        {
            _suppressAutoSave = true;

            var config = ConfigManager.Current;
            Language = config.Language;
            AutoAcceptEula = config.AutoAcceptEula;
            DefaultMinMemoryMb = config.DefaultMinMemoryMb;
            DefaultMaxMemoryMb = config.DefaultMaxMemoryMb;
            DownloadThreads = config.DownloadThreads;
            ConsoleWrapLines = config.ConsoleWrapLines;
            ConsoleFontFamily = string.IsNullOrWhiteSpace(config.ConsoleFontFamily) ? "Consolas" : config.ConsoleFontFamily;
            ConsoleFontSize = Math.Clamp(config.ConsoleFontSize, 10, 32);

            _suppressAutoSave = false;
        }

        private void SaveConfig()
        {
            var minMemory = Math.Max(512, DefaultMinMemoryMb);
            var maxMemory = Math.Max(minMemory, DefaultMaxMemoryMb);
            var downloadThreads = Math.Clamp(DownloadThreads, 1, 32);
            var consoleFontSize = Math.Clamp(ConsoleFontSize, 10, 32);
            var consoleFontFamily = string.IsNullOrWhiteSpace(ConsoleFontFamily) ? "Consolas" : ConsoleFontFamily.Trim();

            var config = ConfigManager.Current;
            config.Language = Language;
            config.AutoAcceptEula = AutoAcceptEula;
            config.DefaultMinMemoryMb = minMemory;
            config.DefaultMaxMemoryMb = maxMemory;
            config.DownloadThreads = downloadThreads;
            config.ConsoleWrapLines = ConsoleWrapLines;
            config.ConsoleFontFamily = consoleFontFamily;
            config.ConsoleFontSize = consoleFontSize;

            _suppressAutoSave = true;
            DefaultMinMemoryMb = minMemory;
            DefaultMaxMemoryMb = maxMemory;
            DownloadThreads = downloadThreads;
            ConsoleFontFamily = consoleFontFamily;
            ConsoleFontSize = consoleFontSize;
            _suppressAutoSave = false;

            ConfigManager.Save();
            StatusMessage = "设置已自动保存";

            // 更新下载管理器并发数
            DownloadManager.ReconfigureDefault(downloadThreads);
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            var config = new AppConfig();
            _suppressAutoSave = true;

            Language = config.Language;
            AutoAcceptEula = config.AutoAcceptEula;
            DefaultMinMemoryMb = config.DefaultMinMemoryMb;
            DefaultMaxMemoryMb = config.DefaultMaxMemoryMb;
            DownloadThreads = config.DownloadThreads;
            ConsoleWrapLines = config.ConsoleWrapLines;
            ConsoleFontFamily = config.ConsoleFontFamily;
            ConsoleFontSize = config.ConsoleFontSize;

            _suppressAutoSave = false;
            SaveConfig();
            StatusMessage = "已重置为默认值";
        }

        partial void OnLanguageChanged(string value) => QueueAutoSave();

        partial void OnAutoAcceptEulaChanged(bool value) => QueueAutoSave();

        partial void OnDefaultMinMemoryMbChanged(int value) => QueueAutoSave();

        partial void OnDefaultMaxMemoryMbChanged(int value) => QueueAutoSave();

        partial void OnDownloadThreadsChanged(int value) => QueueAutoSave();

        partial void OnConsoleWrapLinesChanged(bool value) => QueueAutoSave();

        partial void OnConsoleFontFamilyChanged(string value) => QueueAutoSave();

        partial void OnConsoleFontSizeChanged(int value) => QueueAutoSave();

        private void QueueAutoSave()
        {
            if (_suppressAutoSave || !_isInitialized)
            {
                return;
            }

            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();
        }

        private void InitializeAutoSaveTimer()
        {
            if (_autoSaveTimer != null)
            {
                return;
            }

            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };

            _autoSaveTimer.Tick += (_, _) =>
            {
                _autoSaveTimer.Stop();
                SaveConfig();
            };
        }

        private void LoadConsoleFontFamilies()
        {
            ConsoleFontFamilies.Clear();

            foreach (var fontFamily in Fonts.SystemFontFamilies
                .Select(static family => family.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static family => family))
            {
                ConsoleFontFamilies.Add(fontFamily);
            }
        }

        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        [RelayCommand]
        private void ChangeTheme(string parameter)
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
    }
}
