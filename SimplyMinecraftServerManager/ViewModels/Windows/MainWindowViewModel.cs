// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Models;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Collections.ObjectModel;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray;

namespace SimplyMinecraftServerManager.ViewModels.Windows
{
    /// <summary>
    /// 主窗口的视图模型，管理导航菜单和应用程序状态
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        /// <summary>
        /// 应用程序标题
        /// </summary>
        [ObservableProperty]
        private string _applicationTitle = "SMSM v1.0 Beta";

        /// <summary>
        /// 主导航菜单项集合
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<object> _menuItems;

        /// <summary>
        /// 底部导航菜单项集合
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems;

        /// <summary>
        /// 系统托盘菜单项集合
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems;

        /// <summary>
        /// 当前进行中的下载任务数量
        /// </summary>
        [ObservableProperty]
        private int _downloadTaskCount = 0;

        /// <summary>
        /// 下载任务导航项（用于显示角标）
        /// </summary>
        private readonly NavigationViewItem? _downloadTasksNavItem;

        /// <summary>
        /// 下载视图模型实例
        /// </summary>
        private readonly DownloadsViewModel _downloadsViewModel;

        /// <summary>
        /// 应用通知服务实例
        /// </summary>
        private readonly AppNotificationService _notificationService;

        /// <summary>
        /// 获取通知集合
        /// </summary>
        public ObservableCollection<AppNotificationItem> Notifications => _notificationService.Notifications;

        /// <summary>
        /// 初始化主窗口视图模型
        /// </summary>
        /// <param name="downloadsViewModel">下载视图模型</param>
        /// <param name="notificationService">通知服务</param>
        public MainWindowViewModel(DownloadsViewModel downloadsViewModel, AppNotificationService notificationService)
        {
            _downloadsViewModel = downloadsViewModel;
            _notificationService = notificationService;
            
            // 订阅下载任务事件
            _downloadsViewModel.TaskCountChanged += OnTaskCountChanged;
            
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
                    Content = "运行时管理",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.DeveloperBoard24 },
                    TargetPageType = typeof(Views.Pages.JdkPage)
                }
            ];

            _footerMenuItems =
            [
                new NavigationViewItem()
                {
                    Content = "任务",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowDownload24 },
                    TargetPageType = typeof(Views.Pages.DownloadsPage)
                },
                new NavigationViewItem()
                {
                    Content = "工具",
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Toolbox24 },
                    TargetPageType = typeof(Views.Pages.ToolsPage)
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
                // 在文本中显示任务数量
                if (count > 0)
                {
                    _downloadTasksNavItem.Content = $"任务 ({count})";
                }
                else
                {
                    _downloadTasksNavItem.Content = "任务";
                }
            }
        }

        /// <summary>
        /// 任务数量变化事件处理
        /// </summary>
        private void OnTaskCountChanged(object? sender, int count)
        {
            UpdateDownloadTaskBadge(count);
        }

        /// <summary>
        /// 刷新实例菜单（在运行时管理下面添加实例入口）
        /// </summary>
        public void RefreshInstanceMenus()
        {
            var instances = InstanceManager.GetAll();
            var newItems = new List<object>();
            foreach (var instance in instances)
            {
                newItems.Add(new NavigationViewItem()
                {
                    Content = instance.Name,
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Box24 },
                    TargetPageType = typeof(Views.Pages.InstancePage),
                    Tag = instance.Id
                });
            }

            while (MenuItems.Count > 4)
            {
                MenuItems.RemoveAt(MenuItems.Count - 1);
            }

            foreach (var item in newItems)
            {
                MenuItems.Add(item);
            }
        }
    }
}
