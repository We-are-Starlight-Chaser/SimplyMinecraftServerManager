// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Services;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
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

        public async Task OnNavigatedToAsync()
        {
            await LoadServersAsync();
            LoadStatistics();
            LoadJdkStatus();
            LoadAnnouncements();
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

[RelayCommand]
        private async Task LoadServersAsync()
        {
            try
            {
                Servers.Clear();
                
                var instances = InstanceManager.GetAll();
                var runningInstances = ServerProcessManager.GetRunningInstanceIds();
                var runningSet = new HashSet<string>(runningInstances);
                
                RunningServersCount = runningInstances.Count;
                TotalServersCount = instances.Count;

                await Task.Run(() =>
                {
                    foreach (var inst in instances)
                    {
                        var isRunning = runningSet.Contains(inst.Id);
                        var metadata = ServerJarMetadataReader.Read(inst);
                        var serverItem = new ServerDisplayItem
                        {
                            Name = inst.Name,
                            ServerType = metadata.ServerType,
                            MinecraftVersion = metadata.MinecraftVersion,
                            IsRunning = isRunning,
                            InstanceId = inst.Id
                        };
                        Servers.Add(serverItem);
                    }
                });
            }
            catch
            {
            }
        }

        private void LoadStatistics()
        {
            StatisticsText = $"共 {TotalServersCount} 个服务器，{RunningServersCount} 个正在运行";
        }

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

        private void LoadAnnouncements()
        {
            Announcements.Clear();
            Announcements.Add("支持 Paper、Folia、Purpur、Leaves、Leaf 等常见服务端。");
            Announcements.Add("新建实例时会自动初始化基础配置，并自动分配可用端口。");
            Announcements.Add("JDK 可由管理器统一安装和切换。");
        }

        [RelayCommand]
        private void NavigateToServers()
        {
            _navigationService.Navigate(typeof(Views.Pages.ServersPage));
        }

        [RelayCommand]
        private void NavigateToDownloads()
        {
            _navigationService.Navigate(typeof(Views.Pages.DownloadPage));
        }

        [RelayCommand]
        private void NavigateToSettings()
        {
            _navigationService.Navigate(typeof(Views.Pages.SettingsPage));
        }

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
