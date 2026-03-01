using System.Collections.ObjectModel;
using Microsoft.Win32;
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
        private ObservableCollection<ServerVersionCard> _serverVersionCards = new();

        [ObservableProperty]
        private bool _isLoadingVersions = false;

        [ObservableProperty]
        private string _serverDownloadStatus = "";

        [ObservableProperty]
        private string _currentPlatformName = "Paper";

        [ObservableProperty]
        private string _currentPlatformDescription = "高性能 Paper 服务端";

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
            ServerVersionCards.Clear();
            ServerDownloadStatus = "正在加载版本列表...";

            try
            {
                var platform = GetSelectedServerPlatform();
                if (platform == null) return;

                var platformInfo = GetSelectedPlatformInfo();
                CurrentPlatformName = platformInfo.Name;
                CurrentPlatformDescription = platformInfo.Description;

                var versions = await platform.GetVersionsAsync();

                // 只取最新 15 个版本
                var versionsToLoad = versions.Take(15).ToList();
                var cards = new List<ServerVersionCard>();

                // 使用信号量限制并发数，避免请求过多失败
                var semaphore = new SemaphoreSlim(5, 5); // 最多5个并发

                var tasks = versionsToLoad.Select(async v =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var builds = await platform.GetBuildsAsync(v);
                        var latestBuild = builds.FirstOrDefault();

                        if (latestBuild != null)
                        {
                            return new ServerVersionCard
                            {
                                MinecraftVersion = v,
                                LatestBuild = latestBuild,
                                PlatformName = platformInfo.Name,
                                PlatformColorLight = platformInfo.ColorLight,
                                PlatformColorDark = platformInfo.ColorDark
                            };
                        }
                    }
                    catch
                    {
                        // 单个版本加载失败，跳过
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                    return null;
                });

                var results = await Task.WhenAll(tasks);
                
                // 按版本号正确排序（使用 Version 类比较）
                var sortedCards = results
                    .Where(c => c != null)
                    .Select(c => c!)
                    .OrderByDescending(c => ParseVersionNumber(c.MinecraftVersion));

                foreach (var card in sortedCards)
                {
                    ServerVersionCards.Add(card);
                }

                ServerDownloadStatus = $"已加载 {ServerVersionCards.Count} 个版本";
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

        /// <summary>
        /// 解析 Minecraft 版本号为可比较的对象
        /// </summary>
        private static Version ParseVersionNumber(string version)
        {
            // 移除可能的前缀如 "1.21.4-pre1" 或 "21w37a"
            var dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
            {
                version = version.Substring(0, dashIndex);
            }

            // 尝试解析为标准版本号
            if (Version.TryParse(version, out var v))
            {
                return v;
            }

            // 如果解析失败，返回 0.0
            return new Version(0, 0);
        }

        [RelayCommand]
        private async Task DownloadServerVersionAsync(ServerVersionCard? card)
        {
            if (card?.LatestBuild == null) return;

            try
            {
                var platform = GetSelectedServerPlatform();
                if (platform == null) return;

                string destPath = System.IO.Path.Combine(
                    PathHelper.Root,
                    "downloads",
                    card.LatestBuild.FileName);

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath)!);

                await platform.DownloadAsync(card.LatestBuild, destPath);
                ServerDownloadStatus = $"已添加下载任务: {card.LatestBuild.FileName}";
            }
            catch (Exception ex)
            {
                ServerDownloadStatus = $"下载失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SaveServerVersionAsAsync(ServerVersionCard? card)
        {
            if (card?.LatestBuild == null) return;

            try
            {
                var saveDialog = new SaveFileDialog
                {
                    FileName = card.LatestBuild.FileName,
                    Filter = "JAR 文件 (*.jar)|*.jar|所有文件 (*.*)|*.*",
                    Title = "保存服务端文件"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var platform = GetSelectedServerPlatform();
                    if (platform == null) return;

                    ServerDownloadStatus = $"正在下载: {card.LatestBuild.FileName}";
                    await platform.DownloadAsync(card.LatestBuild, saveDialog.FileName);
                    ServerDownloadStatus = $"下载完成: {saveDialog.FileName}";
                }
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

        private (string Name, string Description, string ColorLight, string ColorDark) GetSelectedPlatformInfo()
        {
            // 使用更柔和的颜色，浅色模式用较深色，深色模式用较浅色
            return SelectedServerPlatformIndex switch
            {
                0 => ("Paper", "优化性能，减少延迟", "#C9A227", "#E8C547"),      // 金色
                1 => ("Folia", "Paper 分支，支持多线程", "#3D8B40", "#5CB85C"),   // 绿色
                2 => ("Purpur", "Paper 分支，更多功能选项", "#7B1FA2", "#9C27B0"), // 紫色
                3 => ("Leaves", "Paper 分支，支持 Carpet 模块", "#558B2F", "#7CB342"), // 浅绿
                4 => ("Leaf", "Leaves 分支，性能优化", "#0097A7", "#00BCD4"),     // 青色
                _ => ("Paper", "优化性能，减少延迟", "#C9A227", "#E8C547")
            };
        }

        partial void OnSelectedServerPlatformIndexChanged(int value)
        {
            _ = LoadServerVersionsAsync();
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
    }

    #region 辅助类

    /// <summary>
    /// 服务端版本卡片数据
    /// </summary>
    public class ServerVersionCard
    {
        /// <summary>Minecraft 版本号</summary>
        public string MinecraftVersion { get; init; } = "";

        /// <summary>最新构建</summary>
        public ServerBuild LatestBuild { get; init; } = null!;

        /// <summary>平台名称</summary>
        public string PlatformName { get; init; } = "";

        /// <summary>平台主题色 (浅色模式)</summary>
        public string PlatformColorLight { get; init; } = "#C9A227";

        /// <summary>平台主题色 (深色模式)</summary>
        public string PlatformColorDark { get; init; } = "#E8C547";

        /// <summary>构建号显示文本</summary>
        public string BuildNumberText => $"#{LatestBuild?.BuildNumber ?? 0}";

        /// <summary>渠道标签</summary>
        public string ChannelTag => LatestBuild?.Channel ?? "default";

        /// <summary>是否为实验性版本</summary>
        public bool IsExperimental => LatestBuild?.Channel == "experimental";

        /// <summary>显示的版本标签 (如 1.21.4)</summary>
        public string VersionTag => $"MC {MinecraftVersion}";
    }

    #endregion
}
