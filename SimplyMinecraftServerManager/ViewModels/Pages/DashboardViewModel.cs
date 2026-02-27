using System.Collections.ObjectModel;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
        [ObservableProperty]
        private string _welcomeText = "欢迎使用 Simply Minecraft Server Manager";

        [ObservableProperty]
        private string _statisticsText = "";

        [ObservableProperty]
        private ObservableCollection<QuickInstanceItem> _recentInstances = new();

        [ObservableProperty]
        private ObservableCollection<string> _announcements = new();

        [ObservableProperty]
        private string _jdkStatus = "";

        public async Task OnNavigatedToAsync()
        {
            LoadStatistics();
            LoadRecentInstances();
            CheckJdkStatus();
            LoadAnnouncements();
            await Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void LoadStatistics()
        {
            var instances = InstanceManager.GetAll();
            // TODO: 实际运行中的服务器数量

            StatisticsText = $"共 {instances.Count} 个服务器实例";
        }

        private void LoadRecentInstances()
        {
            RecentInstances.Clear();
            var instances = InstanceManager.GetAll().Take(5);
            foreach (var inst in instances)
            {
                RecentInstances.Add(new QuickInstanceItem
                {
                    Id = inst.Id,
                    Name = inst.Name,
                    ServerType = inst.ServerType,
                    MinecraftVersion = inst.MinecraftVersion
                });
            }
        }

        private void CheckJdkStatus()
        {
            var jdks = JdkManager.GetInstalledJdks();
            if (jdks.Count == 0)
            {
                JdkStatus = "⚠️ 未检测到已安装的 JDK，请前往 JDK 管理页面安装";
            }
            else
            {
                JdkStatus = $"✅ 已安装 {jdks.Count} 个 JDK 版本";
            }
        }

        private void LoadAnnouncements()
        {
            // 这里可以从网络加载公告，目前使用硬编码
            Announcements.Clear();
            Announcements.Add("欢迎使用 SMSMss！");
            Announcements.Add("建议先在 JDK 管理页面安装 JDK，然后创建服务器实例。");
            Announcements.Add("如遇问题，请访问项目主页获取帮助。");
        }

        [RelayCommand]
        private void NavigateToSite(string uri)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
    }

    public class QuickInstanceItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ServerType { get; set; } = "";
        public string MinecraftVersion { get; set; } = "";
    }
}