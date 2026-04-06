using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Services;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class JdkViewModel : ObservableObject, INavigationAware
    {
        private readonly IContentDialogService _contentDialogService;
        private readonly AppNotificationService _notificationService;

        [ObservableProperty]
        private ObservableCollection<InstalledJdkDisplayItem> _installedJdks = [];

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        private int _selectedJdkDistributionIndex = 0;

        [ObservableProperty]
        private ObservableCollection<int> _availableJdkVersions = [];

        [ObservableProperty]
        private int _selectedJdkMajorVersion = 21;

        [ObservableProperty]
        private bool _isLoadingVersions = false;

        [ObservableProperty]
        private string _installStatus = "";

        public JdkViewModel(IContentDialogService contentDialogService, AppNotificationService notificationService)
        {
            _contentDialogService = contentDialogService;
            _notificationService = notificationService;
        }

        public async Task OnNavigatedToAsync()
        {
            LoadInstalledJdks();
            await LoadAvailableJdkVersionsAsync();
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

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

    public partial class InstalledJdkDisplayItem : ObservableObject
    {
        public JdkDistribution Distribution { get; }
        public int MajorVersion { get; }
        public string FullVersion { get; }
        public JdkArchitecture Architecture { get; }
        public string HomePath { get; }
        public string JavaExecutable { get; }
        public bool IsValid { get; }

        public string DistributionName => Distribution.ToString();

        public string StatusText => IsValid ? "有效" : "无效";

        public string RawStatus => IsValid ? "Valid" : "Invalid";

        [ObservableProperty]
        private bool _isCopied = false;

        public void ShowCopied()
        {
            IsCopied = true;
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                Application.Current.Dispatcher.Invoke(() => IsCopied = false);
            });
        }

        public InstalledJdkDisplayItem(InstalledJdk jdk)
        {
            Distribution = jdk.Distribution;
            MajorVersion = jdk.MajorVersion;
            FullVersion = jdk.FullVersion;
            Architecture = jdk.Architecture;
            HomePath = jdk.HomePath;
            JavaExecutable = jdk.JavaExecutable;
            IsValid = jdk.IsValid;
        }
    }
}
