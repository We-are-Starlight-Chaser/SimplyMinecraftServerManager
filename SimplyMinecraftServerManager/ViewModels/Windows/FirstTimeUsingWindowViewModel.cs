using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Models;
using System.Collections.ObjectModel;

namespace SimplyMinecraftServerManager.ViewModels.Windows
{
    public partial class FirstTimeUsingWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial string ApplicationTitle { get; set; } = $"{Assets.AppProperties.AppTitle} - 新手教程";
        [ObservableProperty]
        public partial bool IsNextButtonAvailable { get; set; } = true;

        [ObservableProperty]
        public partial bool IsBackButtonAvailable { get; set; } = false;

        [ObservableProperty]
        public partial FirstTimeUsingTipItem? SelectedTip { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<FirstTimeUsingTipItem> Tips { get; set; }
        [ObservableProperty]
        public partial int TipIndex { get; set; }

        public FirstTimeUsingWindowViewModel()
        {
            Tips =
            [
                new FirstTimeUsingTipItem
                {
                    Title = "欢迎使用 SMSM",
                    Description = "Simply Minecraft Server Manager，一款简洁的 Minecraft 服务器管理工具。\n让我们来看看如何管理您的服务器吧。",
                    Tips = []
                },
                new FirstTimeUsingTipItem
                {
                    Title = "下载服务端",
                    Description = "在「下载」页面，您可以一键下载各种 Minecraft 服务端：",
                    Tips =
                    [
                        new TipItem { Feature = "Paper — 高性能服务端，兼容 Bukkit/Spigot 插件" },
                        new TipItem { Feature = "Folia — 高性能服务端，在Paper的基础上，添加多线程支持" },
                        new TipItem { Feature = "Purpur — 基于 Paper，提供更多自定义选项" },
                        new TipItem { Feature = "Fabric — 轻量级模组加载器" },
                        new TipItem { Feature = "NeoForge — Forge 的社区分支" },
                        new TipItem { Feature = "自动匹配最新稳定版本，支持版本筛选" }
                    ]
                },
                new FirstTimeUsingTipItem
                {
                    Title = "创建与管理实例",
                    Description = "下载服务端后，可以在「服务端管理」中创建实例：",
                    Tips =
                    [
                        new TipItem { Feature = "为实例命名，设置内存、端口等参数" },
                        new TipItem { Feature = "一键启动/停止服务器，实时查看控制台输出" },
                        new TipItem { Feature = "自动接受 EULA，简化启动流程" },
                        new TipItem { Feature = "支持安装 CurseForge / Modrinth 模组与插件" }
                    ]
                },
                new FirstTimeUsingTipItem
                {
                    Title = "运行时管理",
                    Description = "在「运行时管理」页面管理 Java 运行环境：",
                    Tips =
                    [
                        new TipItem { Feature = "自动检测已安装的 JDK" },
                        new TipItem { Feature = "支持自动下载 Adoptium / Zulu JDK" },
                        new TipItem { Feature = "每个实例可独立指定 JDK 版本" }
                    ]
                },
                new FirstTimeUsingTipItem
                {
                    Title = "准备就绪",
                    Description = "您已了解 SMSM 的核心功能，现在就开始创建您的第一个 Minecraft 服务器吧！",
                    Tips =
                    [
                        new TipItem { Feature = "底部「任务」页可查看下载进度" },
                        new TipItem { Feature = "底部「工具」页提供更多实用功能" },
                        new TipItem { Feature = "底部「设置」页可调整语言、内存默认值等" }
                    ]
                }
            ];
            SelectedTip = Tips[0];
        }

        partial void OnSelectedTipChanged(FirstTimeUsingTipItem? value)
        {
            if (Tips is null || Tips.Count == 0)
            {
                TipIndex = 0;
                return;
            }

            TipIndex = value is not null ? Tips.IndexOf(value) + 1: 0;
        }

        [RelayCommand]
        private void NextTip()
        {
            if (SelectedTip is null) return;

            int currentIndex = Tips.IndexOf(SelectedTip);
            if (currentIndex < Tips.Count - 1)
            {
                SelectedTip = Tips[currentIndex +1];
            }

            UpdateButtonState();
        }
        [RelayCommand]
        private void PrevTip()
        {
            if (SelectedTip is null) return;

            int currentIndex = Tips.IndexOf(SelectedTip);
            if (currentIndex > 0)
            {
                SelectedTip = Tips[currentIndex - 1];
            }

            UpdateButtonState();
        }

        [RelayCommand]
        private void FinishTip()
        {
            ConfigManager.Current.IsFirstTimeUsing = false;
            ConfigManager.Save(ConfigManager.Current);
            WeakReferenceMessenger.Default.Send(new FirstTimeUsingCompletedMessage());
        }
        private void UpdateButtonState()
        {
            if (SelectedTip is null)
            {
                IsBackButtonAvailable = false;
                IsNextButtonAvailable = false;
                return;
            }

            int idx = Tips.IndexOf(SelectedTip);
            IsBackButtonAvailable = idx > 0;
            IsNextButtonAvailable = idx < Tips.Count - 1;
        }
        public sealed class FirstTimeUsingCompletedMessage : ValueChangedMessage<bool>
        {
            public FirstTimeUsingCompletedMessage() : base(true) { }
        }
    }
}
