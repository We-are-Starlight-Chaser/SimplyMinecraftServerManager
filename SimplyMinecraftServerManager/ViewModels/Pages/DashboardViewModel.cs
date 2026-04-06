using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Services;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
        private readonly INavigationService _navigationService;
        private readonly NavigationParameterService _navigationParameterService;

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
        private ObservableCollection<ServerDisplayItem> _recentInstances = [];

        [ObservableProperty]
        private ObservableCollection<string> _announcements = [];

        public DashboardViewModel(INavigationService navigationService, NavigationParameterService navigationParameterService)
        {
            _navigationService = navigationService;
            _navigationParameterService = navigationParameterService;
        }

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

                // 统计运行中的服务器
                var runningInstances = ServerProcessManager.GetRunningInstanceIds();
                RunningServersCount = runningInstances.Count;
                TotalServersCount = instances.Count;

                foreach (var inst in instances)
                {
                    var isRunning = runningInstances.Contains(inst.Id);
                    var serverItem = new ServerDisplayItem
                    {
                        Name = inst.Name,
                        ServerType = inst.ServerType,
                        MinecraftVersion = inst.MinecraftVersion,
                        IsRunning = isRunning,
                        InstanceId = inst.Id
                    };
                    Servers.Add(serverItem);
                }

                // 更新最近实例，取最新的5个
                RecentInstances.Clear();
                var recent = instances
                    .OrderByDescending(static inst =>
                        DateTime.TryParse(inst.CreatedAt, out var createdAt) ? createdAt : DateTime.MinValue)
                    .Take(5)
                    .ToList();
                foreach (var inst in recent)
                {
                    var isRunning = runningInstances.Contains(inst.Id);
                    var serverItem = new ServerDisplayItem
                    {
                        Name = inst.Name,
                        ServerType = inst.ServerType,
                        MinecraftVersion = inst.MinecraftVersion,
                        IsRunning = isRunning,
                        InstanceId = inst.Id
                    };
                    RecentInstances.Add(serverItem);
                }
            }
            catch
            {
                // 忽略加载错误
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
