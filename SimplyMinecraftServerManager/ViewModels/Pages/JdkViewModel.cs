// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Services;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    /// <summary>
    /// JDK 管理页面的视图模型，负责 JDK 的安装、卸载和管理。
    /// </summary>
    public partial class JdkViewModel(IContentDialogService contentDialogService, AppNotificationService notificationService) : ObservableObject, INavigationAware
    {
        /// <summary>内容对话框服务。</summary>
        private readonly IContentDialogService _contentDialogService = contentDialogService;

        /// <summary>应用通知服务。</summary>
        private readonly AppNotificationService _notificationService = notificationService;

        /// <summary>已安装的 JDK 列表。</summary>
        [ObservableProperty]
        private ObservableCollection<InstalledJdkDisplayItem> _installedJdks = [];

        /// <summary>指示是否正在执行安装或卸载操作。</summary>
        [ObservableProperty]
        private bool _isLoading = false;

        /// <summary>状态消息文本。</summary>
        [ObservableProperty]
        private string _statusMessage = "";

        /// <summary>当前选中的 JDK 发行版索引（0=Adoptium，1=Zulu）。</summary>
        [ObservableProperty]
        private int _selectedJdkDistributionIndex = 0;

        /// <summary>可用的 JDK 主版本号列表。</summary>
        [ObservableProperty]
        private ObservableCollection<int> _availableJdkVersions = [];

        /// <summary>当前选中的 JDK 主版本号。</summary>
        [ObservableProperty]
        private int _selectedJdkMajorVersion = 21;

        /// <summary>指示是否正在加载可用版本列表。</summary>
        [ObservableProperty]
        private bool _isLoadingVersions = false;

        /// <summary>安装状态信息文本。</summary>
        [ObservableProperty]
        private string _installStatus = "";

        /// <summary>
        /// 导航到此页面时加载已安装的 JDK 列表和可用版本。
        /// </summary>
        public async Task OnNavigatedToAsync()
        {
            LoadInstalledJdks();
            await LoadAvailableJdkVersionsAsync();
        }

        /// <summary>
        /// 离开此页面时执行的清理操作。
        /// </summary>
        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        /// <summary>
        /// 从 JdkManager 加载已安装的 JDK 列表。
        /// </summary>
        private void LoadInstalledJdks()
        {
            InstalledJdks.Clear();

            try
            {
                var jdks = JdkManager.GetInstalledJdks();
                foreach (var jdk in jdks)
                {
                    InstalledJdks.Add(new InstalledJdkDisplayItem(jdk));
                }
                StatusMessage = $"已安装 {jdks.Count} 个 JDK";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 加载当前选中发行版的可用 JDK 版本列表。
        /// </summary>
        [RelayCommand]
        private async Task LoadAvailableJdkVersionsAsync()
        {
            if (IsLoadingVersions) return;

            IsLoadingVersions = true;
            AvailableJdkVersions.Clear();

            try
            {
                var distribution = SelectedJdkDistributionIndex == 0
                    ? JdkDistribution.Adoptium
                    : JdkDistribution.Zulu;

                var provider = JdkProviderFactory.Get(distribution);
                var versions = await provider.GetAvailableMajorVersionsAsync();

                foreach (var v in versions)
                {
                    AvailableJdkVersions.Add(v);
                }

                if (AvailableJdkVersions.Count > 0 && !AvailableJdkVersions.Contains(SelectedJdkMajorVersion))
                {
                    SelectedJdkMajorVersion = AvailableJdkVersions[0];
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载版本列表失败: {ex.Message}";
            }
            finally
            {
                IsLoadingVersions = false;
            }
        }

        /// <summary>
        /// 安装选中的 JDK 版本。
        /// </summary>
        [RelayCommand]
        private async Task InstallJdkAsync()
        {
            if (IsLoading) return;

            IsLoading = true;
            InstallStatus = $"正在安装 JDK {SelectedJdkMajorVersion}...";

            try
            {
                var distribution = SelectedJdkDistributionIndex == 0
                    ? JdkDistribution.Adoptium
                    : JdkDistribution.Zulu;

                _notificationService.ShowInfo(
                    "任务已创建",
                    $"开始下载 JDK {distribution} {SelectedJdkMajorVersion}...");

                var installed = await JdkManager.AutoInstallAsync(
                    SelectedJdkMajorVersion,
                    distribution,
                    progress: new Progress<int>(p =>
                    {
                        InstallStatus = p < 50
                            ? $"正在下载... {p * 2}%"
                            : $"正在解压... {(p - 50) * 2}%";
                    })
                );

                InstallStatus = $"安装完成: {installed.JavaExecutable}";
                _notificationService.ShowSuccess(
                    "任务已完成",
                    $"已安装 JDK {installed.Distribution} {installed.FullVersion}。");
                LoadInstalledJdks();
            }
            catch (Exception ex)
            {
                InstallStatus = $"安装失败: {ex.Message}";
                _notificationService.ShowDanger(
                    "任务失败",
                    $"JDK {SelectedJdkMajorVersion} 安装失败：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 卸载指定的 JDK（需确认）。
        /// </summary>
        [RelayCommand]
        private async Task UninstallJdkAsync(InstalledJdkDisplayItem? item)
        {
            if (item == null) return;

            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除 JDK {item.MajorVersion} ({item.DistributionName}) 吗？\n\n路径: {item.HomePath}",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await _contentDialogService.ShowAsync(dialog, CancellationToken.None);

            if (result != ContentDialogResult.Primary) return;

            try
            {
                bool success = JdkManager.Uninstall(item.MajorVersion, item.Distribution, item.Architecture);
                if (success)
                {
                    InstalledJdks.Remove(item);
                    StatusMessage = $"JDK {item.MajorVersion} 已卸载";
                }
                else
                {
                    StatusMessage = "卸载失败: 未找到 JDK";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"卸载失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 将指定 JDK 的 Java 可执行文件路径复制到剪贴板。
        /// </summary>
        [RelayCommand]
        private void CopyJavaPath(InstalledJdkDisplayItem? item)
        {
            if (item == null) return;

            try
            {
                Clipboard.SetText(item.JavaExecutable);
                item.ShowCopied();
            }
            catch
            {
                // 忽略复制失败
            }
        }

        /// <summary>
        /// 刷新已安装的 JDK 列表。
        /// </summary>
        [RelayCommand]
        private void Refresh()
        {
            LoadInstalledJdks();
        }

        partial void OnSelectedJdkDistributionIndexChanged(int value)
        {
            _ = LoadAvailableJdkVersionsAsync();
        }
    }

    /// <summary>
    /// 已安装 JDK 的显示项，封装 JDK 信息用于界面绑定。
    /// </summary>
    public partial class InstalledJdkDisplayItem(InstalledJdk jdk) : ObservableObject
    {
        /// <summary>JDK 发行版。</summary>
        public JdkDistribution Distribution { get; } = jdk.Distribution;

        /// <summary>JDK 主版本号。</summary>
        public int MajorVersion { get; } = jdk.MajorVersion;

        /// <summary>JDK 完整版本字符串。</summary>
        public string FullVersion { get; } = jdk.FullVersion;

        /// <summary>CPU 架构。</summary>
        public JdkArchitecture Architecture { get; } = jdk.Architecture;

        /// <summary>JDK 安装根目录路径。</summary>
        public string HomePath { get; } = jdk.HomePath;

        /// <summary>Java 可执行文件路径。</summary>
        public string JavaExecutable { get; } = jdk.JavaExecutable;

        /// <summary>JDK 安装是否有效。</summary>
        public bool IsValid { get; } = jdk.IsValid;

        /// <summary>发行版名称文本。</summary>
        public string DistributionName => Distribution.ToString();

        /// <summary>有效性状态文本。</summary>
        public string StatusText => IsValid ? "有效" : "无效";

        /// <summary>有效性状态英文文本。</summary>
        public string RawStatus => IsValid ? "Valid" : "Invalid";

        /// <summary>指示路径是否已复制（用于显示复制成功提示）。</summary>
        [ObservableProperty]
        private bool _isCopied = false;

        /// <summary>
        /// 显示"已复制"提示，2 秒后自动消失。
        /// </summary>
        public async void ShowCopied()
        {
            IsCopied = true;
            await Task.Delay(2000);
            Application.Current?.Dispatcher.BeginInvoke(() => IsCopied = false);
        }
    }
}
