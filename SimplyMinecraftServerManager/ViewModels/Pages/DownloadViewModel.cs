using Microsoft.Win32;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.Views.Pages;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class DownloadViewModel : ObservableObject, INavigationAware
    {
        #region 服务端下载

        [ObservableProperty]
        private int _selectedServerPlatformIndex = 0;

        [ObservableProperty]
        private ObservableCollection<ServerVersionCard> _serverVersionCards = [];

        [ObservableProperty]
        private bool _isLoadingVersions = false;

        [ObservableProperty]
        private bool _isLoadingMore = false;

        [ObservableProperty]
        private bool _hasMoreVersions = false;

        [ObservableProperty]
        private string _serverDownloadStatus = "";

        [ObservableProperty]
        private string _currentPlatformName = "Paper";

        [ObservableProperty]
        private string _currentPlatformDescription = "高性能 Paper 服务端";

        // 分页相关字段
        private List<string> _allVersions = [];
        private int _loadedVersionCount = 0;
        private const int _pageSize = 15;
        private CancellationTokenSource? _loadCancellationTokenSource;

        #endregion

        #region 插件搜索下载

        [ObservableProperty]
        private string _pluginSearchQuery = "";

        [ObservableProperty]
        private ObservableCollection<ModrinthProject> _pluginSearchResults = [];

        [ObservableProperty]
        private bool _isSearchingPlugins = false;

        [ObservableProperty]
        private string _pluginSearchStatus = "";

        [ObservableProperty]
        private string _pluginTargetInstanceId = "";

        [ObservableProperty]
        private ObservableCollection<InstanceInfo> _availableInstances = [];

        #endregion

        #region JDK 下载

        [ObservableProperty]
        private int _selectedJdkDistributionIndex = 0;

        [ObservableProperty]
        private ObservableCollection<int> _availableJdkVersions = [];

        [ObservableProperty]
        private int _selectedJdkMajorVersion = 21;

        [ObservableProperty]
        private ObservableCollection<JdkInfo> _jdkBuilds = [];

        [ObservableProperty]
        private bool _isLoadingJdkVersions = false;

        [ObservableProperty]
        private bool _isLoadingJdkBuilds = false;

        [ObservableProperty]
        private string _jdkDownloadStatus = "";

        #endregion

        private readonly Dictionary<ServerPlatform, IServerProvider> _serverProviders = [];
        private readonly IContentDialogService _contentDialogService;
        private readonly INavigationService _navigationService;
        private readonly NavigationParameterService _navigationParameterService;

        public DownloadViewModel(
            IContentDialogService contentDialogService,
            INavigationService navigationService,
            NavigationParameterService navigationParameterService)
        {
            _contentDialogService = contentDialogService;
            _navigationService = navigationService;
            _navigationParameterService = navigationParameterService;

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

        public Task OnNavigatedFromAsync()
        {
            // 离开页面时取消所有加载任务
            _loadCancellationTokenSource?.Cancel();
            return Task.CompletedTask;
        }

        #region 服务端下载方法

        [RelayCommand]
        private async Task LoadServerVersionsAsync()
        {
            if (IsLoadingVersions) return;

            // 取消之前的加载任务
            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _loadCancellationTokenSource.Token;

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

                // 获取所有版本
                var allVersions = await platform.GetVersionsAsync();
                _allVersions = allVersions.ToList();
                _loadedVersionCount = 0;
                HasMoreVersions = _allVersions.Count > 0;

                // 检查是否已取消
                cancellationToken.ThrowIfCancellationRequested();

                // 加载第一页
                await LoadVersionsPageAsync(0, _pageSize, cancellationToken);
                
                ServerDownloadStatus = $"已加载 {ServerVersionCards.Count} 个版本，共 {_allVersions.Count} 个版本";
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，不显示错误
                ServerDownloadStatus = "加载已取消";
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
        private async Task LoadMoreVersionsAsync()
        {
            if (IsLoadingMore || !HasMoreVersions) return;

            // 使用相同的取消令牌源
            if (_loadCancellationTokenSource == null || _loadCancellationTokenSource.IsCancellationRequested)
            {
                _loadCancellationTokenSource = new CancellationTokenSource();
            }
            var cancellationToken = _loadCancellationTokenSource.Token;

            IsLoadingMore = true;
            try
            {
                await LoadVersionsPageAsync(_loadedVersionCount, _pageSize, cancellationToken);
                ServerDownloadStatus = $"已加载 {ServerVersionCards.Count} 个版本，共 {_allVersions.Count} 个版本";
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，不显示错误
                ServerDownloadStatus = "加载已取消";
            }
            catch (Exception ex)
            {
                ServerDownloadStatus = $"加载更多版本失败: {ex.Message}";
            }
            finally
            {
                IsLoadingMore = false;
            }
        }

        /// <summary>
        /// 加载指定范围的版本
        /// </summary>
        private async Task LoadVersionsPageAsync(int startIndex, int count, CancellationToken cancellationToken = default)
        {
            if (startIndex >= _allVersions.Count) return;

            var platform = GetSelectedServerPlatform();
            if (platform == null) return;

            var platformInfo = GetSelectedPlatformInfo();
            
            // 计算要加载的版本范围
            var endIndex = Math.Min(startIndex + count, _allVersions.Count);
            var versionsToLoad = _allVersions.Skip(startIndex).Take(endIndex - startIndex).ToList();
            
            if (versionsToLoad.Count == 0) return;

            var cards = new List<ServerVersionCard>();
            using var semaphore = new SemaphoreSlim(5, 5);

            var tasks = versionsToLoad.Select(async v =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                catch (OperationCanceledException)
                {
                    // 任务被取消，重新抛出
                    throw;
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

            // 检查是否已取消
            cancellationToken.ThrowIfCancellationRequested();

            // 按版本号正确排序（使用 Version 类比较）
            var sortedCards = results
                .Where(c => c != null)
                .Select(c => c!)
                .OrderByDescending(c => ParseVersionNumber(c.MinecraftVersion));

            foreach (var card in sortedCards)
            {
                ServerVersionCards.Add(card);
            }

            _loadedVersionCount = endIndex;
            HasMoreVersions = _loadedVersionCount < _allVersions.Count;
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
        private async Task DownloadAndCreateAsync(ServerVersionCard? card)
        {
            if (card?.LatestBuild == null) return;

            try
            {
                var platform = GetSelectedServerPlatform();
                if (platform == null) return;

                // 首先下载服务端文件到临时位置
                string tempPath = System.IO.Path.Combine(
                    PathHelper.Root,
                    "downloads",
                    card.LatestBuild.FileName);

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tempPath)!);

                ServerDownloadStatus = $"正在下载服务端: {card.LatestBuild.FileName}";
                await platform.DownloadAsync(card.LatestBuild, tempPath);
                ServerDownloadStatus = $"下载完成，准备创建实例...";

                // 获取平台名称
                var platformName = GetSelectedPlatformInfo().Name.ToLowerInvariant();
                
                // 创建对话框ViewModel
                var availableVersions = new ObservableCollection<string> { card.MinecraftVersion };
                var availableJdks = new ObservableCollection<JdkDisplayItem>();
                
                // 加载已安装的JDK
                var installedJdks = JdkManager.GetInstalledJdks();
                foreach (var jdk in installedJdks)
                {
                    availableJdks.Add(new JdkDisplayItem(jdk));
                }

                NewInstanceDialogViewModel? dialogViewModel = null;
                NewInstanceDialog? dialog = null;
                
                dialogViewModel = new NewInstanceDialogViewModel(
                    availableVersions,
                    availableJdks,
                    async () =>
                    {
                        if (dialogViewModel != null && dialog != null)
                            await CreateInstanceFromDownloadAsync(dialogViewModel, dialog, tempPath, platformName, card);
                    },
                    () => { }
                );

                // 设置默认值
                dialogViewModel.ServerType = platformName;
                dialogViewModel.SelectedVersion = card.MinecraftVersion;

                dialog = new NewInstanceDialog
                {
                    DataContext = dialogViewModel
                };

                await _contentDialogService.ShowAsync(dialog, CancellationToken.None);
            }
            catch (Exception ex)
            {
                ServerDownloadStatus = $"操作失败: {ex.Message}";
            }
        }

        private async Task CreateInstanceFromDownloadAsync(
            NewInstanceDialogViewModel vm,
            NewInstanceDialog dialog,
            string serverJarPath,
            string serverType,
            ServerVersionCard card)
        {
            if (string.IsNullOrWhiteSpace(vm.InstanceName))
            {
                vm.StatusMessage = "请输入实例名称";
                return;
            }

            try
            {
                vm.IsCreating = true;
                vm.StatusMessage = "正在创建实例...";

                string? javaPath = null;

                if (vm.SelectedJdk != null && System.IO.File.Exists(vm.SelectedJdk.JavaPath))
                {
                    javaPath = vm.SelectedJdk.JavaPath;
                    vm.StatusMessage = $"正在使用已选择的 JDK {vm.SelectedJdk.MajorVersion}...";
                }
                else
                {
                    int jdkVersion = JdkManager.RecommendJdkVersion(vm.SelectedVersion ?? card.MinecraftVersion);
                    javaPath = JdkManager.GetJavaExecutable(jdkVersion);

                    if (string.IsNullOrEmpty(javaPath))
                    {
                        vm.StatusMessage = $"正在安装 JDK {jdkVersion}...";
                        javaPath = await JdkManager.EnsureJdkAsync(jdkVersion);
                    }
                }

                string serverJarFileName;
                string sourceJarPath;

                // 检查是否使用自定义JAR文件
                if (vm.UseCustomJar && !string.IsNullOrWhiteSpace(vm.CustomJarPath) && System.IO.File.Exists(vm.CustomJarPath))
                {
                    // 使用自定义JAR文件
                    serverJarFileName = System.IO.Path.GetFileName(vm.CustomJarPath);
                    sourceJarPath = vm.CustomJarPath;
                    vm.StatusMessage = "使用自定义JAR文件...";
                }
                else
                {
                    // 使用下载的JAR文件
                    serverJarFileName = System.IO.Path.GetFileName(serverJarPath);
                    sourceJarPath = serverJarPath;
                    vm.StatusMessage = "使用下载的服务端文件...";
                }

                // 创建实例
                var instance = InstanceManager.CreateInstance(
                    name: vm.InstanceName,
                    serverType: serverType,
                    minecraftVersion: vm.SelectedVersion ?? card.MinecraftVersion,
                    jdkPath: javaPath,
                    serverJar: serverJarFileName,
                    minMemoryMb: vm.MinMemory,
                    maxMemoryMb: vm.MaxMemory
                );

                // 将JAR文件复制到实例目录
                string instanceJarPath = PathHelper.GetServerJarPath(instance.Id, serverJarFileName);
                System.IO.File.Copy(sourceJarPath, instanceJarPath, true);

                // 关闭对话框并显示成功消息
                dialog.Hide();
                ServerDownloadStatus = $"实例 \"{instance.Name}\" 创建成功";
            }
            catch (Exception ex)
            {
                vm.StatusMessage = $"创建失败: {ex.Message}";
            }
            finally
            {
                vm.IsCreating = false;
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
                0 => ("Paper", "高性能 Fork，异步区块加载，优化红石/实体 AI，Bukkit/Spigot 插件兼容。", "#C9A227", "#E8C547"),      // 金色
                1 => ("Folia", "Paper 分支，区域化多线程调度，独立 Tick 分区，适合大型多人高并发。新手勿选", "#3D8B40", "#5CB85C"),   // 绿色
                2 => ("Purpur", "Paper 下游，高度可配置，自定义实体行为/游戏机制，更丰富的玩法开关。", "#7B1FA2", "#9C27B0"), // 紫色
                3 => ("Leaves", "Paper 下游，兼容 Fabric 协议 Mod，假人支持，轻量级生电向优化。", "#558B2F", "#7CB342"), // 浅绿
                4 => ("Leaf", "Leaves/Gale 融合，多线程实体追踪，异步寻路，极致性能压榨方案。", "#0097A7", "#00BCD4"),     // 青色
                _ => ("Paper", "高性能 Fork，异步区块加载，优化红石/实体 AI，Bukkit/Spigot 插件兼容。", "#C9A227", "#E8C547")
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
