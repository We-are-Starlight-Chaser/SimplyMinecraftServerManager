// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Services;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    /// <summary>
    /// 仪表盘页面的视图模型，负责显示服务器统计信息、JDK 状态和系统公告。
    /// </summary>
    public partial class DashboardViewModel(INavigationService navigationService, NavigationParameterService navigationParameterService) : ObservableObject, INavigationAware
    {
        private readonly INavigationService _navigationService = navigationService;
        private readonly NavigationParameterService _navigationParameterService = navigationParameterService;

        [ObservableProperty]
        private ObservableCollection<ServerDisplayItem> _servers = [];

        [ObservableProperty]
        private string _welcomeText = "欢迎使用 Simply Minecraft Server Manager";

        [ObservableProperty]
        private int _runningServersCount = 0;

        [ObservableProperty]
        private int _totalServersCount = 0;

        [ObservableProperty]
        private string _statisticsText = "";

        [ObservableProperty]
        private string _jdkStatus = "";

        [ObservableProperty]
        private ObservableCollection<string> _announcements = [];

        /// <summary>
        /// 导航到此页面时执行，加载服务器列表、统计信息、JDK 状态和公告。
        /// </summary>
        public async Task OnNavigatedToAsync()
        {
            await LoadServersAsync();
            LoadStatistics();
            LoadJdkStatus();
            LoadAnnouncements();
        }

        /// <summary>
        /// 从此页面导航离开时执行。
        /// </summary>
        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        /// <summary>
        /// 异步加载服务器实例列表，更新服务器数量和运行状态统计。
        /// </summary>
        [RelayCommand]
        private async Task LoadServersAsync()
        {
            try
            {
                var instances = InstanceManager.GetAll();
                var runningInstances = ServerProcessManager.GetRunningInstanceIds();
                var runningSet = new HashSet<string>(runningInstances);
                
                RunningServersCount = runningInstances.Count;
                TotalServersCount = instances.Count;

                var items = await Task.Run(() =>
                {
                    var result = new ConcurrentBag<ServerDisplayItem>();
                    Parallel.ForEach(instances, inst =>
                    {
                        var isRunning = runningSet.Contains(inst.Id);
                        var metadata = ServerJarMetadataReader.Read(inst);
                        result.Add(new ServerDisplayItem
                        {
                            Name = inst.Name,
                            ServerType = metadata.ServerType,
                            MinecraftVersion = metadata.MinecraftVersion,
                            IsRunning = isRunning,
                            InstanceId = inst.Id
                        });
                    });
                    return result.OrderBy(x => x.Name).ToList();
                });

                Servers = new ObservableCollection<ServerDisplayItem>(items);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 加载服务器统计信息文本。
        /// </summary>
        private void LoadStatistics()
        {
            StatisticsText = $"共 {TotalServersCount} 个服务器，{RunningServersCount} 个正在运行";
        }

        /// <summary>
        /// 加载 JDK 安装状态信息。
        /// </summary>
        private void LoadJdkStatus()
        {
            try
            {
                var jdks = JdkManager.GetInstalledJdks();
                JdkStatus = $"已安装 {jdks.Count} 个 JDK";
            }
            catch
            {
                JdkStatus = "JDK 状态未知";
            }
        }

        /// <summary>
        /// 加载系统公告列表。
        /// </summary>
        private void LoadAnnouncements()
        {
            Announcements.Clear();
            Announcements.Add("支持 Paper、Folia、Purpur、Leaves、Leaf 等常见服务端。");
            Announcements.Add("新建实例时会自动初始化基础配置，并自动分配可用端口。");
            Announcements.Add("JDK 可由管理器统一安装和切换。");
        }

        /// <summary>
        /// 导航到服务器管理页面。
        /// </summary>
        [RelayCommand]
        private void NavigateToServers()
        {
            _navigationService.Navigate(typeof(Views.Pages.ServersPage));
        }

        /// <summary>
        /// 导航到下载页面。
        /// </summary>
        [RelayCommand]
        private void NavigateToDownloads()
        {
            _navigationService.Navigate(typeof(Views.Pages.DownloadPage));
        }

        /// <summary>
        /// 导航到设置页面。
        /// </summary>
        [RelayCommand]
        private void NavigateToSettings()
        {
            _navigationService.Navigate(typeof(Views.Pages.SettingsPage));
        }

        /// <summary>
        /// 在默认浏览器中打开指定的 URL 链接。
        /// </summary>
        /// <param name="url">要打开的网站 URL 地址。</param>
        [RelayCommand]
        private void NavigateToSite(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    /// <summary>
    /// 服务器的显示项，封装服务器信息用于仪表盘页面的 UI 显示。
    /// </summary>
    public partial class ServerDisplayItem : ObservableObject
    {
        public string Name { get; set; } = "";

        public string ServerType { get; set; } = "";

        public string MinecraftVersion { get; set; } = "";

        public string InstanceId { get; set; } = "";

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
