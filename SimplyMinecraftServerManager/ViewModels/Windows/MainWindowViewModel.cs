using SimplyMinecraftServerManager.Internals;
using System.Collections.ObjectModel;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Windows
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "SMSM v1.0 Beta";

        [ObservableProperty]
        private ObservableCollection<object> _menuItems;

        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems;

        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems;

        [ObservableProperty]
        private int _downloadTaskCount = 0;

        /// <summary>
        /// 下载任务导航项（用于显示角标）
        /// </summary>
        private Wpf.Ui.Controls.NavigationViewItem? _downloadTasksNavItem;

        public MainWindowViewModel()
        {
            _menuItems =
            [
                new NavigationViewItem()
                {
                    Content = "主页",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                    TargetPageType = typeof(Views.Pages.DashboardPage)
                },
                new NavigationViewItem()
                {
                    Content = "下载",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowDownload24 },
                    TargetPageType = typeof(Views.Pages.DownloadPage)
                },
                new NavigationViewItem()
                {
                    Content = "服务端管理",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Server24 },
                    TargetPageType = typeof(Views.Pages.ServersPage)
                },
                new NavigationViewItem()
                {
                    Content = "JDK 管理",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.DeveloperBoard24 },
                    TargetPageType = typeof(Views.Pages.JdkPage)
                }
            ];

            _footerMenuItems =
            [
                new NavigationViewItem()
                {
                    Content = "下载任务",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowDownload24 },
                    TargetPageType = typeof(Views.Pages.DownloadsPage)
                },
                new NavigationViewItem()
                {
                    Content = "设置",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                    TargetPageType = typeof(Views.Pages.SettingsPage)
                }
            ];

            // 保存下载任务导航项的引用以便更新角标
            _downloadTasksNavItem = _footerMenuItems[0] as Wpf.Ui.Controls.NavigationViewItem;

            _trayMenuItems =
            [
                new MenuItem { Header = "主页", Tag = "tray_home" }
            ];
        }

        /// <summary>
        /// 更新下载任务角标显示
        /// </summary>
        /// <param name="count">当前进行中的任务数</param>
        public void UpdateDownloadTaskBadge(int count)
        {
            DownloadTaskCount = count;
            if (_downloadTasksNavItem != null)
            {
                _downloadTasksNavItem.Content = count > 0 
                    ? $"下载任务 ({count})" 
                    : "下载任务";
            }
        }

        /// <summary>
        /// 刷新实例菜单（在 JDK 管理下面添加实例入口）
        /// </summary>
        public void RefreshInstanceMenus()
        {
            // 移除旧的实例菜单项（保留前4个固定菜单）
            while (MenuItems.Count > 4)
            {
                MenuItems.RemoveAt(4);
            }

            // 添加实例菜单
            var instances = InstanceManager.GetAll();
            foreach (var instance in instances)
            {
                MenuItems.Add(new NavigationViewItem()
                {
                    Content = instance.Name,
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Box24 },
                    TargetPageType = typeof(Views.Pages.InstancePage),
                    Tag = instance.Id
                });
            }
        }
    }
}
