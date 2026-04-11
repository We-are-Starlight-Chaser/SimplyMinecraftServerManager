using Microsoft.Win32;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.Models;
using SimplyMinecraftServerManager.ViewModels.Dialogs;
using SimplyMinecraftServerManager.Views.Pages;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
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
        private ObservableCollection<PluginTargetInstanceItem> _pluginTargetInstances = [];

        [ObservableProperty]
        private PluginTargetInstanceItem? _selectedPluginTargetInstance;

        [ObservableProperty]
        private bool _hasSelectedPluginTarget = false;

        [ObservableProperty]
        private ObservableCollection<PluginSearchResultCard> _pluginSearchResults = [];

        [ObservableProperty]
        private bool _isSearchingPlugins = false;

        [ObservableProperty]
        private bool _isLoadingMorePlugins = false;

        [ObservableProperty]
        private bool _isLoadingPluginVersions = false;

        [ObservableProperty]
        private string _pluginVersionLoadingMessage = "";

        [ObservableProperty]
        private bool _hasMorePlugins = false;

        [ObservableProperty]
        private string _pluginSearchStatus = "";

        [ObservableProperty]
        private string _pluginTargetPrompt = "先选择目标实例，再按该实例的服务端类型和 Minecraft 版本搜索兼容插件。";

        [ObservableProperty]
        private string _pluginSearchContextText = "未选择目标实例";
        private int _pluginSearchRequestId = 0;
        private CancellationTokenSource? _pluginSearchCancellationTokenSource;

        // 插件分页相关字段
        private List<ModrinthProject> _allPluginResults = [];
        private int _loadedPluginCount = 0;
        private const int _pluginPageSize = 12;
        private int _pluginSearchOffset = 0;
        private int _pluginTotalHits = 0;

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
            LoadPluginTargetInstances();
            await LoadServerVersionsAsync();
        }

        public Task OnNavigatedFromAsync()
        {
            _loadCancellationTokenSource?.Cancel();
            CancelPluginSearch();
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

                var (Name, Description, ColorLight, ColorDark) = GetSelectedPlatformInfo();
                CurrentPlatformName = Name;
                CurrentPlatformDescription = Description;

                // 获取所有版本
                var allVersions = await platform.GetVersionsAsync();
                _allVersions = [.. allVersions];
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

            var (Name, Description, ColorLight, ColorDark) = GetSelectedPlatformInfo();
            
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
                            PlatformName = Name,
                            PlatformColorLight = ColorLight,
                            PlatformColorDark = ColorDark
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
                version = version[..dashIndex];
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
                string destPath = System.IO.Path.Combine(
                    PathHelper.Root,
                    "downloads",
                    card.LatestBuild.FileName);

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath)!);

                DownloadManager.Default.Queue(CreateServerDownloadTask(card, destPath));
                ServerDownloadStatus = $"已创建下载任务: {card.LatestBuild.FileName}";
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
                var res = await platform.DownloadAsync(card.LatestBuild, tempPath);
                if (res.Status == DownloadStatus.Failed || res.Status == DownloadStatus.Cancelled || res.Status == DownloadStatus.Paused) throw new Exception("Download failed!");
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
                )
                {
                    // 设置默认值
                    ServerType = platformName,
                    SelectedVersion = card.MinecraftVersion
                };

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
            InstanceInfo? instance = null;

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
                instance = InstanceManager.CreateInstance(
                    name: vm.InstanceName,
                    serverType: serverType,
                    minecraftVersion: vm.SelectedVersion ?? card.MinecraftVersion,
                    jdkPath: javaPath,
                    serverJar: serverJarFileName,
                    serverJarSourcePath: sourceJarPath,
                    minMemoryMb: vm.MinMemory,
                    maxMemoryMb: vm.MaxMemory
                );

                // 关闭对话框并显示成功消息
                dialog.Hide();
                ServerDownloadStatus = $"实例 \"{instance.Name}\" 创建成功";
            }
            catch (Exception ex)
            {
                if (instance != null)
                {
                    try
                    {
                        InstanceManager.DeleteInstance(instance.Id, deleteFiles: true);
                    }
                    catch
                    {
                    }
                }

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
                    DownloadManager.Default.Queue(CreateServerDownloadTask(card, saveDialog.FileName));
                    ServerDownloadStatus = $"已创建下载任务: {card.LatestBuild.FileName}";
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
        private void SelectPluginTargetInstance(PluginTargetInstanceItem? item)
        {
            SelectedPluginTargetInstance = item;
        }

        [RelayCommand]
        private void SwitchPluginTargetInstance()
        {
            SelectedPluginTargetInstance = null;
        }

        partial void OnSelectedPluginTargetInstanceChanged(PluginTargetInstanceItem? value)
        {
            CancelPluginSearch();
            HasSelectedPluginTarget = value != null;
            PluginSearchResults.Clear();
            _allPluginResults.Clear();
            _loadedPluginCount = 0;
            _pluginSearchOffset = 0;
            _pluginTotalHits = 0;
            HasMorePlugins = false;

            if (value == null)
            {
                PluginSearchContextText = "未选择目标实例";
                PluginSearchStatus = PluginTargetInstances.Count == 0
                    ? "请先创建一个服务器实例，再到这里安装插件。"
                    : "请选择一个目标实例后开始搜索。";
                return;
            }

            PluginSearchContextText = value.SearchContextText;
            PluginSearchStatus = $"已选择 {value.Name}，现在会按 {value.ServerTypeDisplay} / {value.MinecraftVersionDisplay} 搜索兼容插件。";

            if (!string.IsNullOrWhiteSpace(PluginSearchQuery))
            {
                _ = SearchPluginsAsync();
            }
        }

        [RelayCommand]
        private async Task SearchPluginsAsync()
        {
            if (SelectedPluginTargetInstance == null)
            {
                PluginSearchStatus = "请先选择目标实例。";
                return;
            }

            if (string.IsNullOrWhiteSpace(PluginSearchQuery))
            {
                PluginSearchStatus = "请输入插件关键词。";
                return;
            }

            CancelPluginSearch();
            _pluginSearchCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _pluginSearchCancellationTokenSource.Token;
            int requestId = ++_pluginSearchRequestId;
            var targetInstance = SelectedPluginTargetInstance;
            string query = PluginSearchQuery.Trim();

            IsSearchingPlugins = true;
            PluginSearchResults.Clear();
            _allPluginResults.Clear();
            _loadedPluginCount = 0;
            _pluginSearchOffset = 0;
            _pluginTotalHits = 0;
            HasMorePlugins = false;
            PluginSearchStatus = $"正在搜索兼容 {targetInstance.Name} 的插件...";

            try
            {
                var modrinth = new ModrinthProvider();
                var result = await modrinth.SearchAsync(
                    query,
                    loaders: targetInstance.LoaderFilters,
                    gameVersions: targetInstance.VersionFilters,
                    projectType: "plugin",
                    limit: _pluginPageSize,
                    offset: _pluginSearchOffset,
                    ct: cancellationToken);

                if (!IsCurrentPluginSearch(requestId, targetInstance))
                {
                    return;
                }

                _allPluginResults = [.. result.Hits];
                _pluginTotalHits = result.TotalHits;
                _pluginSearchOffset += result.Limit;
                HasMorePlugins = _pluginTotalHits > _allPluginResults.Count;

                await LoadPluginsPageAsync(0, _pluginPageSize, targetInstance);

                if (!IsCurrentPluginSearch(requestId, targetInstance))
                {
                    return;
                }

                PluginSearchStatus = _pluginTotalHits == 0
                    ? $"没有找到兼容 {targetInstance.ServerTypeDisplay} / {targetInstance.MinecraftVersionDisplay} 的结果。"
                    : $"找到 {_pluginTotalHits} 个结果，已展示 {PluginSearchResults.Count} 个。";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (IsCurrentPluginSearch(requestId, targetInstance))
                {
                    PluginSearchStatus = $"搜索失败: {ex.Message}";
                }
            }
            finally
            {
                if (requestId == _pluginSearchRequestId)
                {
                    IsSearchingPlugins = false;
                }
            }
        }

        [RelayCommand]
        private async Task LoadMorePluginsAsync()
        {
            if (IsLoadingMorePlugins || !HasMorePlugins || SelectedPluginTargetInstance == null)
            {
                return;
            }

            IsLoadingMorePlugins = true;
            var targetInstance = SelectedPluginTargetInstance;
            int requestId = _pluginSearchRequestId;
            CancellationToken cancellationToken = _pluginSearchCancellationTokenSource?.Token ?? CancellationToken.None;

            try
            {
                if (_loadedPluginCount >= _allPluginResults.Count && _loadedPluginCount < _pluginTotalHits)
                {
                    var modrinth = new ModrinthProvider();
                    var result = await modrinth.SearchAsync(
                        PluginSearchQuery.Trim(),
                        loaders: targetInstance.LoaderFilters,
                        gameVersions: targetInstance.VersionFilters,
                        projectType: "plugin",
                        limit: _pluginPageSize,
                        offset: _pluginSearchOffset,
                        ct: cancellationToken);

                    if (!IsCurrentPluginSearch(requestId, targetInstance))
                    {
                        return;
                    }

                    _allPluginResults.AddRange(result.Hits);
                    _pluginSearchOffset += result.Limit;
                }

                await LoadPluginsPageAsync(_loadedPluginCount, _pluginPageSize, targetInstance);
                if (!IsCurrentPluginSearch(requestId, targetInstance))
                {
                    return;
                }

                HasMorePlugins = _loadedPluginCount < _pluginTotalHits;
                PluginSearchStatus = $"已展示 {PluginSearchResults.Count} / {_pluginTotalHits} 个结果。";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (IsCurrentPluginSearch(requestId, targetInstance))
                {
                    PluginSearchStatus = $"加载失败: {ex.Message}";
                }
            }
            finally
            {
                IsLoadingMorePlugins = false;
            }
        }

        private void CancelPluginSearch()
        {
            _pluginSearchCancellationTokenSource?.Cancel();
            _pluginSearchCancellationTokenSource?.Dispose();
            _pluginSearchCancellationTokenSource = null;
            IsSearchingPlugins = false;
            IsLoadingMorePlugins = false;
        }

        private bool IsCurrentPluginSearch(int requestId, PluginTargetInstanceItem targetInstance)
        {
            return requestId == _pluginSearchRequestId
                && SelectedPluginTargetInstance?.InstanceId == targetInstance.InstanceId;
        }

        private async Task LoadPluginsPageAsync(int startIndex, int count, PluginTargetInstanceItem targetInstance)
        {
            var endIndex = Math.Min(startIndex + count, _allPluginResults.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                PluginSearchResults.Add(new PluginSearchResultCard(_allPluginResults[i], targetInstance));
                _loadedPluginCount++;

                if ((i - startIndex + 1) % 4 == 0)
                {
                    await Task.Yield();
                }
            }
        }

        [RelayCommand]
        private async Task DownloadPluginAsync(PluginSearchResultCard? card)
        {
            if (card == null || SelectedPluginTargetInstance == null)
            {
                return;
            }

            try
            {
                var selectedVersion = await ShowPluginVersionDialogAsync(card.Project, SelectedPluginTargetInstance, installMode: true);
                if (selectedVersion?.Version.PrimaryFile == null)
                {
                    return;
                }

                QueuePluginInstall(card.Project, selectedVersion.Version, SelectedPluginTargetInstance);
                PluginSearchStatus = $"已将 {card.Title} {selectedVersion.VersionNumber} 加入安装任务。";
            }
            catch (Exception ex)
            {
                PluginSearchStatus = $"安装失败: {ex.Message}";
            }
            finally
            {
                PluginVersionLoadingMessage = "";
                IsLoadingPluginVersions = false;
            }
        }

        [RelayCommand]
        private async Task SavePluginAsAsync(PluginSearchResultCard? card)
        {
            if (card == null || SelectedPluginTargetInstance == null)
            {
                return;
            }

            try
            {
                var selectedVersion = await ShowPluginVersionDialogAsync(card.Project, SelectedPluginTargetInstance, installMode: false);
                if (selectedVersion?.Version.PrimaryFile == null)
                {
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    FileName = selectedVersion.Version.PrimaryFile.FileName,
                    Filter = "JAR 文件 (*.jar)|*.jar|所有文件 (*.*)|*.*",
                    Title = "保存插件文件"
                };

                if (saveDialog.ShowDialog() != true)
                {
                    PluginSearchStatus = "保存已取消。";
                    return;
                }

                QueuePluginSave(card.Project, selectedVersion.Version, saveDialog.FileName);
                PluginSearchStatus = $"已将 {card.Title} {selectedVersion.VersionNumber} 加入下载任务。";
            }
            catch (Exception ex)
            {
                PluginSearchStatus = $"保存失败: {ex.Message}";
            }
            finally
            {
                PluginVersionLoadingMessage = "";
                IsLoadingPluginVersions = false;
            }
        }

        private async Task<PluginVersionListItem?> ShowPluginVersionDialogAsync(
            ModrinthProject project,
            PluginTargetInstanceItem targetInstance,
            bool installMode)
        {
            IsLoadingPluginVersions = true;
            PluginVersionLoadingMessage = $"正在获取 {project.Title} 的可用版本...";
            PluginSearchStatus = PluginVersionLoadingMessage;
            await Task.Yield();

            var versions = await LoadPluginVersionsAsync(project, targetInstance);
            if (versions.Count == 0)
            {
                PluginVersionLoadingMessage = "";
                PluginSearchStatus = $"没有找到适用于 {targetInstance.Name} 的可用版本。";
                return null;
            }

            var dialogViewModel = PluginVersionDialogViewModel.Create(
                project.Title,
                installMode
                    ? $"目标实例：{targetInstance.Name} · {targetInstance.ServerTypeDisplay} · {targetInstance.MinecraftVersionDisplay}"
                    : $"另存为下载 · 当前兼容筛选：{targetInstance.ServerTypeDisplay} · {targetInstance.MinecraftVersionDisplay}",
                installMode ? "安装到实例" : "加入下载任务",
                versions);

            var dialog = new PluginVersionDialog
            {
                DataContext = dialogViewModel
            };

            await _contentDialogService.ShowAsync(dialog, CancellationToken.None);

            if (!dialog.IsConfirmed || dialog.SelectedVersionItem == null)
            {
                PluginVersionLoadingMessage = "";
                PluginSearchStatus = installMode ? "安装已取消。" : "保存已取消。";
                return null;
            }

            PluginVersionLoadingMessage = "";
            return dialog.SelectedVersionItem;
        }

        private static async Task<List<ModrinthVersion>> LoadPluginVersionsAsync(ModrinthProject project, PluginTargetInstanceItem targetInstance)
        {
            var provider = new ModrinthProvider();
            var versions = await provider.GetVersionsAsync(
                project.ProjectId,
                loaders: targetInstance.LoaderFilters,
                gameVersions: targetInstance.VersionFilters);

            if (versions.Count == 0 && targetInstance.LoaderFilters.Count > 0)
            {
                versions = await provider.GetVersionsAsync(project.ProjectId, loaders: targetInstance.LoaderFilters);
            }

            if (versions.Count == 0)
            {
                versions = await provider.GetVersionsAsync(project.ProjectId);
            }

            return await Task.Run(() => versions
                .Where(static version => version.PrimaryFile != null)
                .OrderByDescending(static version => PluginVersionListItem.ParsePublishedDate(version.DatePublished))
                .ToList());
        }

        private static void QueuePluginInstall(ModrinthProject project, ModrinthVersion version, PluginTargetInstanceItem targetInstance)
        {
            var primaryFile = version.PrimaryFile ?? throw new InvalidOperationException("所选版本没有可用主文件。");
            var (expectedHash, hashAlgorithm) = GetPreferredHash(primaryFile);
            var stagingPath = GetPluginStagingPath(targetInstance.InstanceId, primaryFile.FileName);
            var displayName = $"{project.Title} {version.VersionNumber}";

            DownloadManager.Default.Queue(new DownloadTask
            {
                DisplayName = displayName,
                Url = primaryFile.Url,
                DestinationPath = stagingPath,
                ExpectedHash = expectedHash,
                HashAlgorithm = hashAlgorithm,
                Type = TaskType.DownloadAndInstall,
                TargetInstanceId = targetInstance.InstanceId,
                CreatedNotification = TaskNotificationMessage.Info(
                    "任务已创建",
                    $"开始下载 {displayName}..."),
                CompletedNotification = TaskNotificationMessage.Success(
                    "任务已完成",
                    $"已将 {displayName} 安装至实例 {targetInstance.Name}。"),
                FailedNotification = TaskNotificationMessage.Danger(
                    "任务失败",
                    $"{displayName} 安装失败。")
            });
        }

        private static void QueuePluginSave(ModrinthProject project, ModrinthVersion version, string destinationPath)
        {
            var primaryFile = version.PrimaryFile ?? throw new InvalidOperationException("所选版本没有可用主文件。");
            var (expectedHash, hashAlgorithm) = GetPreferredHash(primaryFile);
            var displayName = $"{project.Title} {version.VersionNumber}";

            DownloadManager.Default.Queue(new DownloadTask
            {
                DisplayName = displayName,
                Url = primaryFile.Url,
                DestinationPath = destinationPath,
                ExpectedHash = expectedHash,
                HashAlgorithm = hashAlgorithm,
                CreatedNotification = TaskNotificationMessage.Info(
                    "任务已创建",
                    $"开始下载 {displayName}..."),
                CompletedNotification = TaskNotificationMessage.Success(
                    "任务已完成",
                    $"{displayName} 已下载完成。"),
                FailedNotification = TaskNotificationMessage.Danger(
                    "任务失败",
                    $"{displayName} 下载失败。")
            });
        }

        private static DownloadTask CreateServerDownloadTask(ServerVersionCard card, string destinationPath)
        {
            var displayName = $"{card.PlatformName} {card.MinecraftVersion} #{card.LatestBuild.BuildNumber}";

            return new DownloadTask
            {
                DisplayName = displayName,
                Url = card.LatestBuild.DownloadUrl,
                DestinationPath = destinationPath,
                ExpectedHash = card.LatestBuild.Sha256,
                HashAlgorithm = "SHA256",
                CreatedNotification = TaskNotificationMessage.Info(
                    "任务已创建",
                    $"开始下载 {displayName}..."),
                CompletedNotification = TaskNotificationMessage.Success(
                    "任务已完成",
                    $"{displayName} 已下载完成。"),
                FailedNotification = TaskNotificationMessage.Danger(
                    "任务失败",
                    $"{displayName} 下载失败。")
            };
        }

        private static (string? ExpectedHash, string HashAlgorithm) GetPreferredHash(ModrinthFile file)
        {
            if (file.Hashes.TryGetValue("sha1", out var sha1) && !string.IsNullOrWhiteSpace(sha1))
            {
                return (sha1, "SHA1");
            }

            if (file.Hashes.TryGetValue("sha512", out var sha512) && !string.IsNullOrWhiteSpace(sha512))
            {
                return (sha512, "SHA512");
            }

            return (null, "SHA256");
        }

        private static string GetPluginStagingPath(string instanceId, string fileName)
        {
            string safeFileName = Path.GetFileName(fileName);
            string directory = Path.Combine(PathHelper.Root, "downloads", "plugins", instanceId);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, safeFileName);
        }

        private void LoadPluginTargetInstances()
        {
            PluginTargetInstances.Clear();

            var runningInstances = ServerProcessManager.GetRunningInstanceIds();
            foreach (var instance in InstanceManager.GetAll().OrderBy(static instance => instance.Name, StringComparer.OrdinalIgnoreCase))
            {
                var metadata = ServerJarMetadataReader.Read(instance);
                PluginTargetInstances.Add(new PluginTargetInstanceItem(instance, metadata, runningInstances.Contains(instance.Id)));
            }

            SelectedPluginTargetInstance = null;
            PluginSearchContextText = "未选择目标实例";
            PluginSearchStatus = PluginTargetInstances.Count == 0
                ? "请先创建一个服务器实例，再到这里安装插件。"
                : "请选择一个目标实例后开始搜索。";
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

    public sealed class PluginTargetInstanceItem
    {
        private static readonly IReadOnlyList<string> DefaultPluginLoaders = ["paper", "purpur", "folia", "spigot", "bukkit"];

        public PluginTargetInstanceItem(InstanceInfo instance, ServerJarMetadata metadata, bool isRunning)
        {
            InstanceId = instance.Id;
            Name = instance.Name;
            IsRunning = isRunning;

            ServerTypeDisplay = string.IsNullOrWhiteSpace(metadata.ServerType) ? "未知类型" : metadata.ServerType;
            MinecraftVersionDisplay = string.IsNullOrWhiteSpace(metadata.MinecraftVersion) ? "未知版本" : metadata.MinecraftVersion;
            LoaderFilters = BuildLoaderFilters(metadata.ServerType);
            VersionFilters = !string.IsNullOrWhiteSpace(metadata.MinecraftVersion) && metadata.MinecraftVersion != "未知版本"
                ? [metadata.MinecraftVersion]
                : [];

            SearchContextText = $"目标实例：{Name} · {ServerTypeDisplay} · {MinecraftVersionDisplay}";
            CompatibilityHint = LoaderFilters.Count == 0
                ? "将使用通用 Bukkit / Paper 兼容范围搜索。"
                : $"将按 {string.Join(" / ", LoaderFilters)} 搜索兼容插件。";
        }

        public string InstanceId { get; }

        public string Name { get; }

        public bool IsRunning { get; }

        public string ServerTypeDisplay { get; }

        public string MinecraftVersionDisplay { get; }

        public IReadOnlyList<string> LoaderFilters { get; }

        public IReadOnlyList<string> VersionFilters { get; }

        public string SearchContextText { get; }

        public string CompatibilityHint { get; }

        public string RunningText => IsRunning ? "运行中" : "未运行";

        public string TargetBadgeText => $"{ServerTypeDisplay} · {MinecraftVersionDisplay}";

        public string Initial => string.IsNullOrWhiteSpace(Name) ? "S" : Name[..1].ToUpperInvariant();

        private static IReadOnlyList<string> BuildLoaderFilters(string? serverType)
        {
            if (string.IsNullOrWhiteSpace(serverType) || serverType == "未知类型")
            {
                return DefaultPluginLoaders;
            }

            return serverType.ToLowerInvariant() switch
            {
                "paper" => ["paper", "spigot", "bukkit"],
                "purpur" => ["purpur", "paper", "spigot", "bukkit"],
                "folia" => ["folia", "paper", "spigot", "bukkit"],
                "leaves" => ["paper", "spigot", "bukkit"],
                "leaf" => ["paper", "spigot", "bukkit"],
                "pufferfish" => ["paper", "spigot", "bukkit"],
                "spigot" => ["spigot", "bukkit"],
                "bukkit" => ["bukkit"],
                _ => [serverType.ToLowerInvariant()]
            };
        }
    }

    public sealed class PluginSearchResultCard
    {
        public PluginSearchResultCard(ModrinthProject project, PluginTargetInstanceItem targetInstance)
        {
            Project = project;
            Title = string.IsNullOrWhiteSpace(project.Title) ? project.Slug : project.Title;
            Description = string.IsNullOrWhiteSpace(project.Description) ? "该项目没有提供简介。" : project.Description;
            Author = string.IsNullOrWhiteSpace(project.Author) ? "未知作者" : project.Author;
            Initial = string.IsNullOrWhiteSpace(Title) ? "P" : Title[..1].ToUpperInvariant();
            IconUrl = project.IconUrl;
            HasIcon = !string.IsNullOrWhiteSpace(project.IconUrl);
            DownloadsText = FormatMetric(project.Downloads);
            FollowsText = FormatMetric(project.Follows);
            LatestVersionText = string.IsNullOrWhiteSpace(project.LatestGameVersion) ? "最新支持未知" : $"最新支持 {project.LatestGameVersion}";
            LoadersText = project.Loaders.Count > 0 ? string.Join(" / ", project.Loaders.Take(5)) : "未标注加载器";
            GameVersionsText = FormatGameVersionRange(project.GameVersions);
            CompatibilityText = BuildCompatibilityText(project, targetInstance);
            ServerSideText = string.IsNullOrWhiteSpace(project.ServerSide) ? "服务器支持未知" : $"服务端：{project.ServerSide}";
            ClientSideText = string.IsNullOrWhiteSpace(project.ClientSide) ? "客户端支持未知" : $"客户端：{project.ClientSide}";
        }

        public ModrinthProject Project { get; }

        public string Title { get; }

        public string Description { get; }

        public string Author { get; }

        public string Initial { get; }

        public string IconUrl { get; }

        public bool HasIcon { get; }

        public string DownloadsText { get; }

        public string FollowsText { get; }

        public string LatestVersionText { get; }

        public string LoadersText { get; }

        public string GameVersionsText { get; }

        public string CompatibilityText { get; }

        public string ServerSideText { get; }

        public string ClientSideText { get; }

        private static string BuildCompatibilityText(ModrinthProject project, PluginTargetInstanceItem targetInstance)
        {
            bool loaderCompatible = project.Loaders.Count == 0
                || project.Loaders.Any(loader => targetInstance.LoaderFilters.Contains(loader, StringComparer.OrdinalIgnoreCase));
            bool versionCompatible = targetInstance.VersionFilters.Count == 0
                || project.GameVersions.Count == 0
                || project.GameVersions.Any(version => targetInstance.VersionFilters.Contains(version, StringComparer.OrdinalIgnoreCase));

            if (loaderCompatible && versionCompatible)
            {
                return $"支持 {targetInstance.ServerTypeDisplay} {targetInstance.MinecraftVersionDisplay}";
            }

            if (loaderCompatible)
            {
                return $"加载器兼容，版本标签未覆盖 {targetInstance.MinecraftVersionDisplay}";
            }

            return $"请检查与 {targetInstance.ServerTypeDisplay} 的兼容性";
        }

        private static string FormatMetric(long value)
        {
            if (value >= 1_000_000_000) return $"{value / 1_000_000_000d:F1}B";
            if (value >= 1_000_000) return $"{value / 1_000_000d:F1}M";
            if (value >= 1_000) return $"{value / 1_000d:F1}K";
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatGameVersionRange(List<string>? versions)
        {
            if (versions == null || versions.Count == 0)
            {
                return "未标注版本";
            }

            var ordered = versions
                .Where(static version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static version => ParseComparableVersion(version))
                .ToList();

            if (ordered.Count == 0)
            {
                return "未标注版本";
            }

            if (ordered.Count == 1)
            {
                return ordered[0];
            }

            return $"{ordered[0]} - {ordered[^1]}";
        }

        private static Version ParseComparableVersion(string version)
        {
            var normalized = version;
            var dashIndex = normalized.IndexOf('-');
            if (dashIndex > 0)
            {
                normalized = normalized[..dashIndex];
            }

            return Version.TryParse(normalized, out var parsed)
                ? parsed
                : new Version(0, 0);
        }
    }

    #endregion
}
