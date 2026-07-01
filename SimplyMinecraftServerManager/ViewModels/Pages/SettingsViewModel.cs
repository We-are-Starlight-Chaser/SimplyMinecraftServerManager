// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    /// <summary>
    /// 设置页面的视图模型，管理应用程序配置
    /// </summary>
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        /// <summary>
        /// 是否已完成初始化
        /// </summary>
        private bool _isInitialized = false;

        /// <summary>
        /// 是否抑制自动保存（用于加载配置时避免触发保存）
        /// </summary>
        private bool _suppressAutoSave = false;

        /// <summary>
        /// 自动保存定时器
        /// </summary>
        private DispatcherTimer? _autoSaveTimer;

        /// <summary>
        /// 应用程序版本号
        /// </summary>
        [ObservableProperty]
        private string _appVersion = String.Empty;

        /// <summary>
        /// 当前应用程序主题（深色/浅色）
        /// </summary>
        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

        /// <summary>
        /// 应用程序语言设置
        /// </summary>
        [ObservableProperty]
        private string _language = "zh-CN";

        /// <summary>
        /// 是否自动接受 EULA 协议
        /// </summary>
        [ObservableProperty]
        private bool _autoAcceptEula = true;

        /// <summary>
        /// 默认最小内存分配（MB）
        /// </summary>
        [ObservableProperty]
        private int _defaultMinMemoryMb = 1024;

        /// <summary>
        /// 默认最大内存分配（MB）
        /// </summary>
        [ObservableProperty]
        private int _defaultMaxMemoryMb = 2048;

        /// <summary>
        /// 下载并发线程数
        /// </summary>
        [ObservableProperty]
        private int _downloadThreads = 4;

        /// <summary>
        /// 控制台是否自动换行
        /// </summary>
        [ObservableProperty]
        private bool _consoleWrapLines = false;

        /// <summary>
        /// 控制台字体族
        /// </summary>
        [ObservableProperty]
        private string _consoleFontFamily = "Consolas";

        /// <summary>
        /// 控制台字体大小
        /// </summary>
        [ObservableProperty]
        private int _consoleFontSize = 12;

        /// <summary>
        /// 系统可用的控制台字体列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<string> _consoleFontFamilies = [];

        /// <summary>
        /// 状态消息
        /// </summary>
        [ObservableProperty]
        private string _statusMessage = "";

        /// <summary>
        /// 导航到设置页面时触发
        /// </summary>
        /// <returns>表示异步操作的任务</returns>
        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            return Task.CompletedTask;
        }

        /// <summary>
        /// 从设置页面导航离开时触发
        /// </summary>
        /// <returns>表示异步操作的任务</returns>
        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        /// <summary>
        /// 初始化视图模型，加载系统主题、版本信息和配置
        /// </summary>
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

        /// <summary>
        /// 从配置管理器加载设置到视图模型属性
        /// </summary>
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

        /// <summary>
        /// 将视图模型属性保存到配置管理器
        /// </summary>
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

        /// <summary>
        /// 重置所有设置为默认值
        /// </summary>
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

        /// <summary>
        /// 语言设置变更后触发自动保存
        /// </summary>
        partial void OnLanguageChanged(string value) => QueueAutoSave();

        /// <summary>
        /// 自动接受EULA设置变更后触发自动保存
        /// </summary>
        partial void OnAutoAcceptEulaChanged(bool value) => QueueAutoSave();

        /// <summary>
        /// 最小内存设置变更后触发自动保存
        /// </summary>
        partial void OnDefaultMinMemoryMbChanged(int value) => QueueAutoSave();

        /// <summary>
        /// 最大内存设置变更后触发自动保存
        /// </summary>
        partial void OnDefaultMaxMemoryMbChanged(int value) => QueueAutoSave();

        /// <summary>
        /// 下载线程数设置变更后触发自动保存
        /// </summary>
        partial void OnDownloadThreadsChanged(int value) => QueueAutoSave();

        /// <summary>
        /// 控制台换行设置变更后触发自动保存
        /// </summary>
        partial void OnConsoleWrapLinesChanged(bool value) => QueueAutoSave();

        /// <summary>
        /// 控制台字体族设置变更后触发自动保存
        /// </summary>
        partial void OnConsoleFontFamilyChanged(string value) => QueueAutoSave();

        /// <summary>
        /// 控制台字体大小设置变更后触发自动保存
        /// </summary>
        partial void OnConsoleFontSizeChanged(int value) => QueueAutoSave();

        /// <summary>
        /// 将自动保存操作加入队列，延迟执行
        /// </summary>
        private void QueueAutoSave()
        {
            if (_suppressAutoSave || !_isInitialized)
            {
                return;
            }

            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();
        }

        /// <summary>
        /// 初始化自动保存定时器
        /// </summary>
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

        /// <summary>
        /// 加载系统可用的控制台字体列表
        /// </summary>
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

        /// <summary>
        /// 获取程序集版本号
        /// </summary>
        /// <returns>版本号字符串</returns>
        private static string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        /// <summary>
        /// 切换应用程序主题（深色/浅色）
        /// </summary>
        /// <param name="parameter">主题参数："theme_light" 为浅色，其他为深色</param>
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
