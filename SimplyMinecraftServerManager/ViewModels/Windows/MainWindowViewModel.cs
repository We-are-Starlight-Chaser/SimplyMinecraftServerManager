using System.Collections.ObjectModel;
using SimplyMinecraftServerManager.Internals;
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

        public MainWindowViewModel()
        {
            _menuItems = new ObservableCollection<object>
            {
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
            };

            _footerMenuItems = new ObservableCollection<object>
            {
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
            };

            _trayMenuItems = new ObservableCollection<MenuItem>
            {
                new MenuItem { Header = "主页", Tag = "tray_home" }
            };
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
