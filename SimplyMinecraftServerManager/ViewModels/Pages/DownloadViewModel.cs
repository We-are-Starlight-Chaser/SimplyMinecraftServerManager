using System.Collections.ObjectModel;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class DownloadViewModel : ObservableObject, INavigationAware
    {
        #region 服务端下载

        [ObservableProperty]
        private int _selectedServerPlatformIndex = 0;

        [ObservableProperty]
        private string _selectedMinecraftVersion = "";

        [ObservableProperty]
        private ObservableCollection<string> _minecraftVersions = new();

        [ObservableProperty]
        private ObservableCollection<ServerBuildItem> _serverBuilds = new();

        [ObservableProperty]
        private bool _isLoadingVersions = false;

        [ObservableProperty]
        private bool _isLoadingBuilds = false;

        [ObservableProperty]
        private string _serverDownloadStatus = "";

        #endregion

        #region 插件搜索下载

        [ObservableProperty]
        private string _pluginSearchQuery = "";

        [ObservableProperty]
        private ObservableCollection<ModrinthProject> _pluginSearchResults = new();

        [ObservableProperty]
        private bool _isSearchingPlugins = false;

        [ObservableProperty]
        private string _pluginSearchStatus = "";

        [ObservableProperty]
        private string _pluginTargetInstanceId = "";

        [ObservableProperty]
        private ObservableCollection<InstanceInfo> _availableInstances = new();

        #endregion

        #region JDK 下载

        [ObservableProperty]
        private int _selectedJdkDistributionIndex = 0;

        [ObservableProperty]
        private ObservableCollection<int> _availableJdkVersions = new();

        [ObservableProperty]
        private int _selectedJdkMajorVersion = 21;

        [ObservableProperty]
        private ObservableCollection<JdkInfo> _jdkBuilds = new();

        [ObservableProperty]
        private bool _isLoadingJdkVersions = false;

        [ObservableProperty]
        private bool _isLoadingJdkBuilds = false;

        [ObservableProperty]
        private string _jdkDownloadStatus = "";

        #endregion

        #region 下载任务列表

        [ObservableProperty]
        private ObservableCollection<DownloadTaskItem> _downloadTasks = new();

        #endregion

        private readonly Dictionary<ServerPlatform, IServerProvider> _serverProviders = new();

        public DownloadViewModel()
        {
            // 初始化服务端提供者
            foreach (ServerPlatform platform in Enum.GetValues<ServerPlatform>())
            {
                try
                {
                    _serverProviders[platform] = ServerProviderFactory.Get(platform);
                }
                catch { }
            }
        }

        public async Task OnNavigatedToAsync()
        {
            // 加载可用实例
            AvailableInstances.Clear();
            foreach (var inst in InstanceManager.GetAll())
            {
                AvailableInstances.Add(inst);
            }

            // 初始化下载管理器事件
            DownloadManager.Default.ProgressChanged -= OnDownloadProgress;
            DownloadManager.Default.ProgressChanged += OnDownloadProgress;
            DownloadManager.Default.TaskCompleted -= OnDownloadTaskCompleted;
            DownloadManager.Default.TaskCompleted += OnDownloadTaskCompleted;

            // 刷新下载任务列表
            RefreshDownloadTasks();

            // 加载服务端版本
            await LoadServerVersionsAsync();
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        #region 服务端下载方法

        [RelayCommand]
        private async Task LoadServerVersionsAsync()
        {
            if (IsLoadingVersions) return;

            IsLoadingVersions = true;
            MinecraftVersions.Clear();

            try
            {
                var platform = GetSelectedServerPlatform();
                if (platform == null) return;

                var versions = await platform.GetVersionsAsync();
                foreach (var v in versions)
                {
                    MinecraftVersions.Add(v);
                }

                if (MinecraftVersions.Count > 0)
                {
                    SelectedMinecraftVersion = MinecraftVersions[0];
                    await LoadServerBuildsAsync();
                }
            }
            catch (Exception ex)
            {
                ServerDownloadStatus = $"加载版本失败: {ex.Message}";
            }
            finally
            {
                IsLoadingVersions = false;
            }
        }

        [RelayCommand]
        private async Task LoadServerBuildsAsync()
        {
            if (string.IsNullOrEmpty(SelectedMinecraftVersion) || IsLoadingBuilds) return;

            IsLoadingBuilds = true;
            ServerBuilds.Clear();

            try
            {
                var platform = GetSelectedServerPlatform();
                if (platform == null) return;

                var builds = await platform.GetBuildsAsync(SelectedMinecraftVersion);
                foreach (var b in builds)
                {
                    ServerBuilds.Add(new ServerBuildItem
                    {
                        Build = b,
                        DisplayName = $"#{b.BuildNumber} - {b.Channel}"
                    });
                }
            }
            catch (Exception ex)
            {
                ServerDownloadStatus = $"加载构建失败: {ex.Message}";
            }
            finally
            {
                IsLoadingBuilds = false;
            }
        }

        [RelayCommand]
        private async Task DownloadServerBuildAsync(ServerBuildItem? item)
        {
            if (item?.Build == null) return;

            try
            {
                var platform = GetSelectedServerPlatform();
                if (platform == null) return;

                string destPath = System.IO.Path.Combine(
                    PathHelper.Root,
                    "downloads",
                    item.Build.FileName);

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath)!);

                await platform.DownloadAsync(item.Build, destPath);
                ServerDownloadStatus = $"已添加下载任务: {item.Build.FileName}";
            }
            catch (Exception ex)
            {
                ServerDownloadStatus = $"下载失败: {ex.Message}";
            }
        }

        private IServerProvider? GetSelectedServerPlatform()
        {
            var platforms = new[] {
                ServerPlatform.Paper,
                ServerPlatform.Folia,
                ServerPlatform.Purpur,
                ServerPlatform.Leaves,
                ServerPlatform.Leaf
            };

            int idx = SelectedServerPlatformIndex;
            if (idx < 0 || idx >= platforms.Length) return null;

            return _serverProviders.TryGetValue(platforms[idx], out var p) ? p : null;
        }

        partial void OnSelectedServerPlatformIndexChanged(int value)
        {
            _ = LoadServerVersionsAsync();
        }

        partial void OnSelectedMinecraftVersionChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _ = LoadServerBuildsAsync();
            }
        }

        #endregion

        #region 插件搜索下载方法

        [RelayCommand]
        private async Task SearchPluginsAsync()
        {
            if (string.IsNullOrWhiteSpace(PluginSearchQuery) || IsSearchingPlugins) return;

            IsSearchingPlugins = true;
            PluginSearchResults.Clear();
            PluginSearchStatus = "搜索中...";

            try
            {
                var modrinth = new ModrinthProvider();
                var result = await modrinth.SearchAsync(
                    PluginSearchQuery,
                    loaders: new[] { "bukkit", "spigot", "paper", "purpur" },
                    projectType: "plugin",
                    limit: 20
                );

                foreach (var hit in result.Hits)
                {
                    PluginSearchResults.Add(hit);
                }

                PluginSearchStatus = $"找到 {result.TotalHits} 个结果";
            }
            catch (Exception ex)
            {
                PluginSearchStatus = $"搜索失败: {ex.Message}";
            }
            finally
            {
                IsSearchingPlugins = false;
            }
        }

        [RelayCommand]
        private async Task DownloadPluginAsync(ModrinthProject? project)
        {
            if (project == null) return;

            try
            {
                string? targetInstanceId = PluginTargetInstanceId;
                if (string.IsNullOrEmpty(targetInstanceId) && AvailableInstances.Count > 0)
                {
                    targetInstanceId = AvailableInstances[0].Id;
                }

                if (string.IsNullOrEmpty(targetInstanceId))
                {
                    PluginSearchStatus = "请先创建一个服务器实例";
                    return;
                }

                var modrinth = new ModrinthProvider();
                var versions = await modrinth.GetVersionsAsync(
                    project.ProjectId,
                    loaders: new[] { "bukkit", "spigot", "paper", "purpur" }
                );

                if (versions.Count == 0)
                {
                    PluginSearchStatus = "未找到可用版本";
                    return;
                }

                var latestVersion = versions[0];
                string destPath = System.IO.Path.Combine(
                    PathHelper.GetPluginsDir(targetInstanceId),
                    latestVersion.PrimaryFile!.FileName);

                await modrinth.DownloadVersionAsync(latestVersion, destPath);
                PluginSearchStatus = $"已添加下载任务: {project.Title}";
            }
            catch (Exception ex)
            {
                PluginSearchStatus = $"下载失败: {ex.Message}";
            }
        }

        #endregion

        #region JDK 下载方法

        [RelayCommand]
        private async Task LoadJdkVersionsAsync()
        {
            if (IsLoadingJdkVersions) return;

            IsLoadingJdkVersions = true;
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

                if (AvailableJdkVersions.Count > 0)
                {
                    SelectedJdkMajorVersion = AvailableJdkVersions[0];
                    await LoadJdkBuildsAsync();
                }
            }
            catch (Exception ex)
            {
                JdkDownloadStatus = $"加载版本失败: {ex.Message}";
            }
            finally
            {
                IsLoadingJdkVersions = false;
            }
        }

        [RelayCommand]
        private async Task LoadJdkBuildsAsync()
        {
            if (IsLoadingJdkBuilds) return;

            IsLoadingJdkBuilds = true;
            JdkBuilds.Clear();

            try
            {
                var distribution = SelectedJdkDistributionIndex == 0
                    ? JdkDistribution.Adoptium
                    : JdkDistribution.Zulu;

                var provider = JdkProviderFactory.Get(distribution);
                var builds = await provider.GetBuildsAsync(SelectedJdkMajorVersion);

                foreach (var b in builds)
                {
                    JdkBuilds.Add(b);
                }
            }
            catch (Exception ex)
            {
                JdkDownloadStatus = $"加载构建失败: {ex.Message}";
            }
            finally
            {
                IsLoadingJdkBuilds = false;
            }
        }

        [RelayCommand]
        private async Task DownloadJdkAsync(JdkInfo? jdk)
        {
            if (jdk == null) return;

            try
            {
                JdkDownloadStatus = $"正在下载 JDK {jdk.FullVersion}...";

                var installed = await JdkManager.DownloadAndInstallAsync(
                    jdk,
                    progress: new Progress<int>(p =>
                    {
                        JdkDownloadStatus = $"解压进度: {p}%";
                    })
                );

                JdkDownloadStatus = $"安装完成: {installed.JavaExecutable}";
            }
            catch (Exception ex)
            {
                JdkDownloadStatus = $"安装失败: {ex.Message}";
            }
        }

        partial void OnSelectedJdkDistributionIndexChanged(int value)
        {
            _ = LoadJdkVersionsAsync();
        }

        partial void OnSelectedJdkMajorVersionChanged(int value)
        {
            _ = LoadJdkBuildsAsync();
        }

        #endregion

        #region 下载任务管理

        private void RefreshDownloadTasks()
        {
            DownloadTasks.Clear();
            foreach (var task in DownloadManager.Default.AllTasks)
            {
                DownloadTasks.Add(new DownloadTaskItem(task));
            }
        }

        private void OnDownloadProgress(object? sender, DownloadProgressInfo e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = DownloadTasks.FirstOrDefault(t => t.TaskId == e.TaskId);
                if (item != null)
                {
                    item.UpdateProgress(e);
                }
                else
                {
                    DownloadTasks.Add(new DownloadTaskItem(e));
                }
            });
        }

        private void OnDownloadTaskCompleted(object? sender, DownloadTask task)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = DownloadTasks.FirstOrDefault(t => t.Id == task.Id);
                if (item != null)
                {
                    item.UpdateFromTask(task);
                }
            });
        }

        #endregion
    }

    #region 辅助类

    public class ServerBuildItem
    {
        public ServerBuild Build { get; init; } = null!;
        public string DisplayName { get; init; } = "";
    }

    public partial class DownloadTaskItem : ObservableObject
    {
        public string Id { get; }
        public string TaskId { get; }
        public string DisplayName { get; }

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _status;

        [ObservableProperty]
        private string _speed;

        public DownloadTaskItem(DownloadTask task)
        {
            Id = task.Id;
            TaskId = task.Id;
            DisplayName = task.DisplayName;
            UpdateFromTask(task);
        }

        public DownloadTaskItem(DownloadProgressInfo info)
        {
            TaskId = info.TaskId;
            DisplayName = info.DisplayName;
            Id = TaskId;
            UpdateProgress(info);
        }

        public void UpdateFromTask(DownloadTask task)
        {
            Progress = task.TotalBytes > 0
                ? (double)task.BytesDownloaded / task.TotalBytes * 100
                : 0;
            Status = task.Status.ToString();
            Speed = "";
        }

        public void UpdateProgress(DownloadProgressInfo info)
        {
            Progress = info.ProgressPercent;
            Status = info.IsCompleted ? "已完成" : info.IsFailed ? $"失败: {info.ErrorMessage}" : "下载中";
            Speed = info.SpeedBytesPerSecond > 0
                ? $"{info.SpeedBytesPerSecond / 1024 / 1024:F2} MB/s"
                : "";
        }
    }

    #endregion
}
