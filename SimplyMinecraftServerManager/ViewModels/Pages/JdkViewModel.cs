using System.Collections.ObjectModel;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class JdkViewModel : ObservableObject, INavigationAware
    {
        [ObservableProperty]
        private ObservableCollection<InstalledJdkDisplayItem> _installedJdks = new();

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        private int _selectedJdkDistributionIndex = 0;

        [ObservableProperty]
        private ObservableCollection<int> _availableJdkVersions = new();

        [ObservableProperty]
        private int _selectedJdkMajorVersion = 21;

        [ObservableProperty]
        private bool _isLoadingVersions = false;

        [ObservableProperty]
        private string _installStatus = "";

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
                LoadInstalledJdks();
            }
            catch (Exception ex)
            {
                InstallStatus = $"安装失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void UninstallJdk(InstalledJdkDisplayItem? item)
        {
            if (item == null) return;

            try
            {
                bool success = JdkManager.Uninstall(item.MajorVersion, item.Distribution, item.Architecture);
                if (success)
                {
                    InstalledJdks.Remove(item);
                    StatusMessage = "JDK 已卸载";
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
        private void Refresh()
        {
            LoadInstalledJdks();
        }

        partial void OnSelectedJdkDistributionIndexChanged(int value)
        {
            _ = LoadAvailableJdkVersionsAsync();
        }
    }

    public class InstalledJdkDisplayItem : ObservableObject
    {
        public JdkDistribution Distribution { get; }
        public int MajorVersion { get; }
        public string FullVersion { get; }
        public JdkArchitecture Architecture { get; }
        public string HomePath { get; }
        public string JavaExecutable { get; }
        public bool IsValid { get; }

        public string DistributionName => Distribution.ToString();

        public string StatusText => IsValid ? "✅ 有效" : "❌ 无效";

        public string StatusColor => IsValid ? "#4CAF50" : "#F44336";

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
