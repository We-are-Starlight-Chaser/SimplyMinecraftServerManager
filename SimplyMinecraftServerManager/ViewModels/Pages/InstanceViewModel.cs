// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Microsoft.Win32;
using SharpCompress.Common;
using SharpCompress.Writers.Tar;
using SimplyMinecraftServerManager.Helpers;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Models;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.Views.Pages;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using ZstdNet;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    /// <summary>
    /// 服务器实例详情页面的视图模型，管理实例的启动/停止、控制台、插件、配置、玩家管理和仪表盘等功能。
    /// </summary>
    public partial class InstanceViewModel : ObservableObject, INavigationAware, IDisposable
    {
        /// <summary>JSON 序列化选项，用于格式化输出。</summary>
        private readonly System.Text.Json.JsonSerializerOptions options = new() { WriteIndented = true };

        /// <summary>内容对话框服务。</summary>
        private readonly IContentDialogService _contentDialogService;

        /// <summary>导航服务。</summary>
        private readonly INavigationService _navigationService;

        /// <summary>导航参数服务。</summary>
        private readonly NavigationParameterService _navigationParameterService;

        /// <summary>性能监控器，用于采集 CPU 和内存使用数据。</summary>
        private PerformanceMonitor? _performanceMonitor;

        /// <summary>玩家刷新操作的信号量，防止并发刷新。</summary>
        private readonly SemaphoreSlim _playerRefreshLock = new(1, 1);

        /// <summary>控制台输出操作的锁对象。</summary>
        private readonly Lock _consoleLock = new();


        /// <summary>实例唯一标识符。</summary>
        [ObservableProperty]
        public partial string InstanceId { get; set; } = "";

        private static async void SafeFireAndForget(Task task)
        {
            try { await task; }
            catch (Exception ex) { Debug.WriteLine($"[InstanceViewModel] Background task failed: {ex.Message}"); }
        }

        /// <summary>实例详细信息。</summary>
        [ObservableProperty]
        public partial InstanceInfo? InstanceInfo { get; set; }

        /// <summary>实例显示名称。</summary>
        [ObservableProperty]
        public partial string InstanceName { get; set; } = "加载中...";

        /// <summary>服务端类型（如 Paper、Purpur）。</summary>
        [ObservableProperty]
        public partial string ServerType { get; set; } = "";

        /// <summary>Minecraft 版本号。</summary>
        [ObservableProperty]
        public partial string MinecraftVersion { get; set; } = "";

        /// <summary>服务器是否正在运行。</summary>
        [ObservableProperty]
        public partial bool IsRunning { get; set; } = false;

        /// <summary>服务器是否正在启动中。</summary>
        [ObservableProperty]
        public partial bool IsStarting { get; set; } = false;

        /// <summary>控制台是否自动滚动到底部。</summary>
        [ObservableProperty]
        public partial bool AutoScroll { get; set; } = true;

        /// <summary>控制台是否自动换行。</summary>
        [ObservableProperty]
        public partial bool ConsoleWrapLines { get; set; } = false;

        /// <summary>控制台字体。</summary>
        [ObservableProperty]
        public partial string ConsoleFontFamily { get; set; } = "Consolas";

        /// <summary>控制台字号。</summary>
        [ObservableProperty]
        public partial int ConsoleFontSize { get; set; } = 12;

        /// <summary>控制台是否全屏显示。</summary>
        [ObservableProperty]
        public partial bool IsConsoleFullScreen { get; set; } = false;

        private readonly Queue<string> _consoleLines = new();

        // 控制台内容改变事件，用于通知 UI 更新 FlowDocument
        public event EventHandler<string>? ConsoleLineAdded;
        public event EventHandler? ConsoleCleared;

        /// <summary>控制台最大行数。</summary>
        public static int MaxConsoleLines => 1000;

        /// <summary>控制台命令输入文本。</summary>
        [ObservableProperty]
        public partial string CommandInput { get; set; } = "";

        /// <summary>插件列表。</summary>
        [ObservableProperty]
        public partial ObservableCollection<PluginDisplayItem> Plugins { get; set; } = [];

        /// <summary>服务器属性（server.properties）列表。</summary>
        [ObservableProperty]
        public partial ObservableCollection<ServerProperty> ServerProperties { get; set; } = [];

        /// <summary>状态消息文本。</summary>
        [ObservableProperty]
        public partial string StatusMessage { get; set; } = "";

        // 编辑中的属性
        /// <summary>编辑中的最小内存值（MB）。</summary>
        [ObservableProperty]
        public partial string EditMinMemory { get; set; } = "1024";

        /// <summary>编辑中的最大内存值（MB）。</summary>
        [ObservableProperty]
        public partial string EditMaxMemory { get; set; } = "2048";

        /// <summary>是否使用自定义 JDK 路径。</summary>
        [ObservableProperty]
        public partial bool UseCustomJdk { get; set; } = false;

        /// <summary>是否自动选择 JDK。</summary>
        [ObservableProperty]
        public partial bool AutoSelectJdk { get; set; } = true;

        /// <summary>自定义 JDK 可执行文件路径。</summary>
        [ObservableProperty]
        public partial string EditJdkPath { get; set; } = "";

        /// <summary>额外的 JVM 启动参数。</summary>
        [ObservableProperty]
        public partial string EditExtraJvmArgs { get; set; } = "";

        /// <summary>已安装的 JDK 列表。</summary>
        [ObservableProperty]
        public partial ObservableCollection<InstalledJdk> InstalledJdks { get; set; } = [];

        /// <summary>当前选中的已安装 JDK。</summary>
        [ObservableProperty]
        public partial InstalledJdk? SelectedInstalledJdk { get; set; }

        // 性能监控属性 - 保留原有的性能监控属性
        /// <summary>CPU 使用率百分比。</summary>
        [ObservableProperty]
        public partial double CpuUsage { get; set; } = 0;

        /// <summary>内存使用量（MB）。</summary>
        [ObservableProperty]
        public partial double MemoryUsage { get; set; } = 0;

        /// <summary>总存储空间（MB）。</summary>
        [ObservableProperty]
        public partial long TotalStorageMb { get; set; } = 0;

        /// <summary>总存储空间的格式化文本。</summary>
        [ObservableProperty]
        public partial string TotalStorage { get; set; } = "0 MB";

        /// <summary>各世界文件夹的存储信息。</summary>
        [ObservableProperty]
        public partial ObservableCollection<WorldStorageInfo> WorldStorageInfo { get; set; } = [];

        /// <summary>游戏模式（如生存模式、创造模式）。</summary>
        [ObservableProperty]
        public partial string GameMode { get; set; } = "未知";

        /// <summary>是否启用在线模式（正版验证）。</summary>
        [ObservableProperty]
        public partial bool IsOnlineMode { get; set; } = false;

        /// <summary>模拟距离。</summary>
        [ObservableProperty]
        public partial int SimulationDistance { get; set; } = 0;

        /// <summary>视距。</summary>
        [ObservableProperty]
        public partial int ViewDistance { get; set; } = 0;

        /// <summary>服务器连接地址。</summary>
        [ObservableProperty]
        public partial string ServerAddress { get; set; } = "localhost:25565";

        /// <summary>当前在线玩家数。</summary>
        [ObservableProperty]
        public partial int OnlinePlayersCount { get; set; } = 0;

        /// <summary>最大玩家数上限。</summary>
        [ObservableProperty]
        public partial int MaxPlayersCount { get; set; } = 0;

        /// <summary>在线玩家列表。</summary>
        [ObservableProperty]
        public partial ObservableCollection<PlayerDisplayItem> OnlinePlayers { get; set; } = [];

        /// <summary>管理员（OP）玩家列表。</summary>
        [ObservableProperty]
        public partial ObservableCollection<PlayerDisplayItem> AdminPlayers { get; set; } = [];

        /// <summary>玩家数据文件数量。</summary>
        [ObservableProperty]
        public partial int PlayerDataCount { get; set; } = 0;

        /// <summary>在线玩家提示文本。</summary>
        [ObservableProperty]
        public partial string OnlinePlayersHint { get; set; } = "启动服务器以查看";

        /// <summary>指示是否正在刷新在线玩家列表。</summary>
        [ObservableProperty]
        public partial bool IsRefreshingPlayers { get; set; } = false;

        /// <summary>在线玩家列表为空时是否显示提示。</summary>
        public bool ShowOnlinePlayersHint => OnlinePlayers.Count == 0;

        /// <summary>管理员列表为空时是否显示提示。</summary>
        public bool ShowAdminPlayersHint => AdminPlayers.Count == 0;

        /// <summary>服务器运行时长格式化文本。</summary>
        [ObservableProperty]
        public partial string Uptime { get; set; } = "00:00:00";

        /// <summary>网络已发送字节数。</summary>
        [ObservableProperty]
        public partial long NetworkSentBytes { get; set; } = 0;

        /// <summary>网络已接收字节数。</summary>
        [ObservableProperty]
        public partial long NetworkReceivedBytes { get; set; } = 0;

        /// <summary>备份进度百分比。</summary>
        [ObservableProperty]
        public partial double BackupProgress { get; set; }

        /// <summary>实例最大内存（MB）。</summary>
        public int MaxMemoryMb => InstanceInfo?.MaxMemoryMb ?? 2048;

        /// <summary>服务器状态轮询定时器。</summary>
        private Timer? _serverStatusTimer;

        /// <summary>运行时长计数定时器。</summary>
        private DispatcherTimer? _uptimeTimer;

        /// <summary>服务器启动时间。</summary>
        private DateTime _serverStartTime = DateTime.MinValue;

        /// <summary>定时任务命令列表。</summary>
        private string _scheduledCommands = "";

        private static readonly MemoryCache<List<OpEntry>> _opsCache = new(TimeSpan.FromSeconds(5), 50);

        /// <summary>
        /// 初始化实例详情视图模型。
        /// </summary>
        /// <param name="contentDialogService">内容对话框服务。</param>
        /// <param name="navigationService">导航服务。</param>
        /// <param name="navigationParameterService">导航参数服务。</param>
        public InstanceViewModel(
            IContentDialogService contentDialogService,
            INavigationService navigationService,
            NavigationParameterService navigationParameterService)
        {
            _contentDialogService = contentDialogService;
            _navigationService = navigationService;
            _navigationParameterService = navigationParameterService;
            LoadConsolePreferences();
            OnlinePlayers.CollectionChanged += OnOnlinePlayersCollectionChanged;
            AdminPlayers.CollectionChanged += OnAdminPlayersCollectionChanged;

            // 订阅全局状态变化事件
            ServerProcessManager.InstanceStatusChanged += OnInstanceStatusChanged;
        }

        /// <summary>
        /// 显示定时任务配置对话框。
        /// </summary>
        [RelayCommand]
        private async Task ShowScheduledTaskDialogAsync()
        {
            CancellationTokenSource source = new();
            var dialog = new ScheduledTaskDialog(_scheduledCommands);
            await _contentDialogService.ShowAsync(dialog,source.Token);
            _scheduledCommands = dialog.Commands;
            
        }
        private CancellationTokenSource? _backupCts;

        /// <summary>
        /// 取消正在进行的备份操作。
        /// </summary>
        [RelayCommand]
        private async Task CancelBackup()
        {
            var cts = Volatile.Read(ref _backupCts);
            if (cts != null && !cts.IsCancellationRequested)
            {
                await cts.CancelAsync();
            }
        }

        /// <summary>
        /// 执行服务器实例备份，创建 Zstandard 压缩的 tar 归档文件。
        /// </summary>
        [RelayCommand]
        private async Task BackupAsync()
        {
            var oldCts = Interlocked.Exchange(ref _backupCts, new CancellationTokenSource());
            if (oldCts != null)
            {
                await oldCts.CancelAsync();
                oldCts.Dispose();
            }
            var currentCts = _backupCts!;
            var token = currentCts.Token;
            try
            {
                if (ServerProcessManager.IsRunning(InstanceId) )
                {
                    await ExecutePlayerRconCommandAsync("save-off");
                    await ExecutePlayerRconCommandAsync("save-all flush");
                    await Task.Delay(2500, token);
                }
            }
            catch{
                var res = await _contentDialogService.ShowAsync(new ContentDialog()
                {
                    Title = "SMSM",
                    Content = "无法强制保存服务器文件！您是否要继续？（可能会损坏文件）",
                    PrimaryButtonAppearance = ControlAppearance.Danger,
                    PrimaryButtonText = "确定",
                    CloseButtonText = "取消",
                   CloseButtonAppearance = ControlAppearance.Secondary,
                   
                }, CancellationToken.None);
                if (res != ContentDialogResult.Primary) {
                    return;
                }
            }
            BackupProgress = 0;
 
            string path = PathHelper.GetInstanceDir(InstanceId);
            string destPath = Path.Combine(PathHelper.Root, "backups");
            if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);
            try
            {
                await CreateTarZstdWithProgress(path, Path.Combine(destPath,$"{InstanceId}_{DateTimeOffset.Now:yyyy_MM_dd_HH_mm}.zst"),token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await _contentDialogService.ShowAsync(new ContentDialog()
                {
                    Title = "SMSM",
                    Content = "备份操作已取消！",
                    CloseButtonAppearance = ControlAppearance.Primary,
                    CloseButtonText = "确定"
                }, CancellationToken.None);
            }
            catch
            {
                await _contentDialogService.ShowAsync(new ContentDialog()
                {
                    Title = "SMSM",
                    Content = "备份出现异常！",
                    CloseButtonAppearance = ControlAppearance.Primary,
                    CloseButtonText = "确定"
                }, CancellationToken.None);
                
            }
            finally
            {
                BackupProgress = 100;
                if (ServerProcessManager.IsRunning(InstanceId))
                    await ExecutePlayerRconCommandAsync("save-on");
                if (Interlocked.CompareExchange(ref _backupCts, null, currentCts) == currentCts)
                {
                    currentCts.Dispose();
                }
            }
        }
        /// <summary>
        /// 创建带进度回调的 Zstandard 压缩 tar 归档文件。
        /// </summary>
        public async Task CreateTarZstdWithProgress(string sourceFolder, string outputFilePath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

            long totalSize = 0;
            var fileEntries = new List<(string Path, string Relative, long Length)>();
            var srcDir = new DirectoryInfo(sourceFolder);
            foreach (var fi in srcDir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                var fp = fi.FullName;
                if (fp.Contains("\\logs\\") || fp.EndsWith(".log", StringComparison.OrdinalIgnoreCase)|| fp.EndsWith("session.lock", StringComparison.OrdinalIgnoreCase)|| fp.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)|| fp.EndsWith(".pid", StringComparison.OrdinalIgnoreCase))
                    continue;
                totalSize += fi.Length;
                fileEntries.Add((fp, Path.GetRelativePath(sourceFolder, fp).Replace('\\', '/'), fi.Length));
            }

            long processedSize = 0;

            using var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var zstdStream = new CompressionStream(fileStream, new CompressionOptions(3));
            var writerOptions = new TarWriterOptions(CompressionType.None)
            {
                ArchiveEncoding = new ArchiveEncoding { Default = System.Text.Encoding.UTF8 },
            };

            using var tarWriter = new TarWriter(zstdStream, writerOptions);
            foreach (var (filePath, relativePath, currentFileSize) in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var fileInput = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.SequentialScan);
                using var progressStream = new ProgressStream(fileInput, currentFileSize, bytesRead =>
                { 
                    long newTotal = Interlocked.Add(ref processedSize, bytesRead);
                    BackupProgress = Math.Min(100.0, (double)newTotal / totalSize * 100);
                });
                await tarWriter.WriteAsync(relativePath, progressStream, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken: cancellationToken);
            }
        }
        /// <summary>
        /// 实例运行状态变化时的回调，更新 UI 状态。
        /// </summary>
        private void OnInstanceStatusChanged(object? sender, (string InstanceId, bool IsRunning) e)
        {
            if (e.InstanceId != InstanceId) return;

            RunOnUiThread(() =>
            {
                IsRunning = e.IsRunning;
                IsStarting = false;
                if (!e.IsRunning)
                {
                    StatusMessage = "服务器已停止";
                    StopPerformanceMonitoring();
                    ResetOnlinePlayersState();
                }
                else
                {
                    StatusMessage = "服务器运行中";
                    QueueRunningStateInitialization();
                }
            });
        }

        /// <summary>
        /// 订阅服务器进程的输出和错误事件。
        /// </summary>
        private void SubscribeToProcessOutput()
        {
            var process = ServerProcessManager.GetProcess(InstanceId);
            if (process == null) return;

            // 清除旧的事件订阅（通过克隆进程引用避免重复订阅的问题）
            // 注意：这里使用弱事件模式或确保不重复订阅
            process.OutputReceived -= OnProcessOutputReceived;
            process.ErrorReceived -= OnProcessErrorReceived;

            process.OutputReceived += OnProcessOutputReceived;
            process.ErrorReceived += OnProcessErrorReceived;
        }

        private ThrottledDispatcher? _consoleThrottler;
        private readonly List<string> _pendingConsoleLines = [];
        private readonly Lock _pendingConsoleLock = new();

        /// <summary>
        /// 进程标准输出接收回调，将行添加到待处理队列。
        /// </summary>
        private void OnProcessOutputReceived(object? sender, string line)
        {
            lock (_pendingConsoleLock)
            {
                _pendingConsoleLines.Add(line);
            }
            EnsureConsoleThrottler();
        }

        /// <summary>
        /// 进程标准错误接收回调，将行添加到待处理队列并标记为错误。
        /// </summary>
        private void OnProcessErrorReceived(object? sender, string line)
        {
            lock (_pendingConsoleLock)
            {
                _pendingConsoleLines.Add("[ERR] " + line);
            }
            EnsureConsoleThrottler();
        }

        /// <summary>
        /// 确保控制台节流调度器已初始化。
        /// </summary>
        private void EnsureConsoleThrottler()
        {
            _consoleThrottler ??= new ThrottledDispatcher(DispatcherPriority.Background, 50);
            _consoleThrottler.Invoke(FlushPendingConsoleLines);
        }

        /// <summary>
        /// 将待处理的控制台行批量刷新到控制台输出。
        /// </summary>
        private void FlushPendingConsoleLines()
        {
            List<string> lines;
            lock (_pendingConsoleLock)
            {
                if (_pendingConsoleLines.Count == 0) return;
                lines = [.. _pendingConsoleLines];
                _pendingConsoleLines.Clear();
            }

            foreach (var line in lines)
            {
                AppendConsoleLineInternal(line);
            }
        }

        /// <summary>
        /// 将一行文本添加到控制台缓冲区并触发 UI 更新事件。
        /// </summary>
        private void AppendConsoleLineInternal(string line)
        {
            lock (_consoleLock)
            {
                _consoleLines.Enqueue(line);
                while (_consoleLines.Count > MaxConsoleLines)
                {
                    _consoleLines.Dequeue();
                }
            }
            ConsoleLineAdded?.Invoke(this, line);
        }

        /// <summary>
        /// 向控制台追加一行输出。
        /// </summary>
        private void AppendConsoleLine(string line)
        {
            AppendConsoleLineInternal(line);
        }

        /// <summary>
        /// 在 UI 线程上执行操作。
        /// </summary>
        private static void RunOnUiThread(Action action)
        {
            DispatcherHelper.InvokeIfNeeded(action);
        }

        /// <summary>
        /// 清空控制台内容。
        /// </summary>
        [RelayCommand]
        private void ClearConsole()
        {
            lock (_consoleLock)
            {
                _consoleLines.Clear();
            }
            ConsoleCleared?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 将控制台内容复制到剪贴板。
        /// </summary>
        [RelayCommand]
        private void CopyConsole()
        {
            string text;
            lock (_consoleLock)
            {
                if (_consoleLines.Count == 0) return;
                text = string.Join(Environment.NewLine, _consoleLines);
            }
            System.Windows.Clipboard.SetText(text);
            StatusMessage = "控制台内容已复制到剪贴板";
        }

        /// <summary>
        /// 获取控制台全部内容的文本形式。
        /// </summary>
        public string GetConsoleText()
        {
            lock (_consoleLock)
            {
                if (_consoleLines.Count == 0) return string.Empty;
                return string.Join(Environment.NewLine, _consoleLines);
            }
        }

        /// <summary>
        /// 获取控制台所有行的只读列表。
        /// </summary>
        public IReadOnlyList<string> GetConsoleLines()
        {
            lock (_consoleLock)
            {
                return [.. _consoleLines];
            }
        }

        /// <summary>
        /// 导航到此页面时加载实例数据。
        /// </summary>
        public async Task OnNavigatedToAsync()
        {
            LoadConsolePreferences();

            // 从导航参数服务获取实例 ID
            var instanceId = _navigationParameterService.GetAndClearInstanceId();
            if (!string.IsNullOrEmpty(instanceId))
            {
                LoadInstance(instanceId);
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// 离开此页面时停止监控和清理资源。
        /// </summary>
        public Task OnNavigatedFromAsync()
        {
            IsConsoleFullScreen = false;
            StopPerformanceMonitoring();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 加载指定 ID 的服务器实例数据。
        /// </summary>
        /// <param name="instanceId">实例标识符。</param>
        public void LoadInstance(string instanceId)
        {
            SafeFireAndForget(LoadInstanceAsync(instanceId));
        }

        /// <summary>
        /// 异步加载实例数据，包括基本信息、插件、配置和性能数据。
        /// </summary>
        private async Task LoadInstanceAsync(string instanceId)
        {
            InstanceId = instanceId;
            var info = InstanceManager.GetById(instanceId);
            if (info == null)
            {
                InstanceName = "实例不存在";
                StatusMessage = "实例不存在";
                IsRunning = false;
                return;
            }

            InstanceInfo = info;
            InstanceName = info.Name;
            var metadata = ServerJarMetadataReader.Read(info);
            ServerType = metadata.ServerType;
            MinecraftVersion = metadata.MinecraftVersion;
            EditMinMemory = info.MinMemoryMb.ToString();
            EditMaxMemory = info.MaxMemoryMb.ToString();
            EditJdkPath = info.JdkPath;
            EditExtraJvmArgs = info.ExtraJvmArgs;
            LoadConsolePreferences();

            LoadInstalledJdks();
            InitializeJdkSelectionState(info.JdkPath);

            IsRunning = ServerProcessManager.IsRunning(instanceId);
            StatusMessage = IsRunning ? "服务器运行中" : $"{ServerType} - Minecraft {MinecraftVersion}";

            if (IsRunning)
            {
                QueueRunningStateInitialization();
            }
            else
            {
                LoadStaticStorageInfo();
                ResetOnlinePlayersState();
            }

            _ = Task.Run(() =>
            {
                try { LoadPlugins(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadPlugins failed: {ex.Message}"); }
                try { LoadServerProperties(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadServerProperties failed: {ex.Message}"); }
                try { LoadPlayerManagementData(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadPlayerManagementData failed: {ex.Message}"); }
                try { LoadDashboardData(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadDashboardData failed: {ex.Message}"); }
            });
        }

        /// <summary>
        /// 加载已安装的 JDK 列表。
        /// </summary>
        private void LoadInstalledJdks()
        {
            try
            {
                var installedJdks = JdkManager.GetInstalledJdks();
                InstalledJdks = new ObservableCollection<InstalledJdk>(installedJdks);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载已安装JDK失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从配置加载控制台偏好设置。
        /// </summary>
        private void LoadConsolePreferences()
        {
            var config = ConfigManager.Current;
            ConsoleWrapLines = config.ConsoleWrapLines;
            ConsoleFontFamily = string.IsNullOrWhiteSpace(config.ConsoleFontFamily) ? "Consolas" : config.ConsoleFontFamily;
            ConsoleFontSize = Math.Clamp(config.ConsoleFontSize, 10, 32);
        }

        /// <summary>
        /// 初始化 JDK 选择状态（自动/手动/自定义路径）。
        /// </summary>
        private void InitializeJdkSelectionState(string? jdkPath)
        {
            SelectedInstalledJdk = null;

            if (string.IsNullOrWhiteSpace(jdkPath))
            {
                UseCustomJdk = false;
                AutoSelectJdk = true;
                return;
            }

            var matchingJdk = FindInstalledJdkByPath(jdkPath);
            if (matchingJdk != null)
            {
                UseCustomJdk = false;
                AutoSelectJdk = false;
                SelectedInstalledJdk = matchingJdk;
                return;
            }

            UseCustomJdk = true;
            AutoSelectJdk = false;
        }

        /// <summary>
        /// 根据 JDK 路径在已安装列表中查找匹配项。
        /// </summary>
        private InstalledJdk? FindInstalledJdkByPath(string jdkPath)
        {
            try
            {
                var normalizedTargetPath = Path.GetFullPath(jdkPath);
                return InstalledJdks.FirstOrDefault(jdk =>
                    string.Equals(
                        Path.GetFullPath(jdk.JavaExecutable),
                        normalizedTargetPath,
                        StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return InstalledJdks.FirstOrDefault(jdk =>
                    jdk.JavaExecutable.Equals(jdkPath, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// 加载实例的静态存储空间信息。
        /// </summary>
        private void LoadStaticStorageInfo()
        {
            try
            {
                string instanceDir = PathHelper.GetInstanceDir(InstanceId);
                if (!Directory.Exists(instanceDir)) return;

                // 计算总存储
                long totalBytes = GetDirectorySize(instanceDir);
                TotalStorageMb = totalBytes / (1024 * 1024);
                TotalStorage = FormatBytes(totalBytes);

                // 获取各个世界的大小
                WorldStorageInfo.Clear();
                string[] worldFolders = ["world", "world_nether", "world_the_end"];
                var worldSizes = new Dictionary<string, long>();
                long maxSize = 0;

                foreach (var worldName in worldFolders)
                {
                    string worldPath = Path.Combine(instanceDir, worldName);
                    if (Directory.Exists(worldPath))
                    {
                        long sizeBytes = GetDirectorySize(worldPath);
                        worldSizes[worldName] = sizeBytes / (1024 * 1024);
                        if (worldSizes[worldName] > maxSize)
                            maxSize = worldSizes[worldName];
                    }
                }

                foreach (var kvp in worldSizes)
                {
                    WorldStorageInfo.Add(new WorldStorageInfo
                    {
                        WorldName = kvp.Key switch
                        {
                            "world" => "主世界",
                            "world_nether" => "下界",
                            "world_the_end" => "末地",
                            _ => kvp.Key
                        },
                        SizeMb = kvp.Value,
                        SizeFormatted = FormatBytes(kvp.Value * 1024 * 1024),
                        SizePercent = maxSize > 0 ? (double)kvp.Value / maxSize * 100 : 0
                    });
                }

                StatusMessage = "存储空间统计已刷新";
            }
            catch { }
        }

        /// <summary>
        /// 刷新存储空间信息。
        /// </summary>
        [RelayCommand]
        private void RefreshStorageInfo()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                LoadStaticStorageInfo();
            }
            catch (Exception ex)
            {
                StatusMessage = $"刷新存储信息失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 递归获取目录及其子目录的总大小（字节）。
        /// </summary>
        private static long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                var dir = new DirectoryInfo(path);
                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { size += file.Length; }
                    catch { }
                }
            }
            catch { }
            return size;
        }

        /// <summary>
        /// 加载玩家管理相关数据（OP 列表、在线玩家等）。
        /// </summary>
        private void LoadPlayerManagementData()
        {
            LoadPlayerDataCount();
            LoadAdminPlayers();

            if (IsRunning)
            {
                _ = RefreshOnlinePlayersAsync();
            }
            else
            {
                ResetOnlinePlayersState();
            }
        }

        /// <summary>
        /// 加载玩家数据文件计数。
        /// </summary>
        private void LoadPlayerDataCount()
        {
            try
            {
                var properties = ServerPropertiesManager.Read(InstanceId);
                string levelName = properties.GetValueOrDefault("level-name", "world");
                if (string.IsNullOrWhiteSpace(levelName))
                {
                    levelName = "world";
                }

                string playerDataDir = Path.Combine(PathHelper.GetInstanceDir(InstanceId), levelName, "playerdata");
                PlayerDataCount = Directory.Exists(playerDataDir)
                    ? Directory.EnumerateFiles(playerDataDir, "*.dat", SearchOption.TopDirectoryOnly).Count()
                    : 0;
            }
            catch
            {
                PlayerDataCount = 0;
            }
        }

        /// <summary>
        /// 加载管理员（OP）玩家列表。
        /// </summary>
        private void LoadAdminPlayers()
        {
            var ops = ReadOps();
            RunOnUiThread(() =>
            {
                AdminPlayers.Clear();
                foreach (var op in ops)
                {
                    AdminPlayers.Add(new PlayerDisplayItem(op.Name, op.Uuid)
                    {
                        IsOp = true,
                        SecondaryText = $"等级 {op.Level}" + (string.IsNullOrWhiteSpace(op.Uuid) ? string.Empty : $"  UUID {op.Uuid}")
                    });
                }
            });
        }

        /// <summary>
        /// 从 ops.json 读取管理员列表。
        /// </summary>
        private List<OpEntry> ReadOps()
        {
            if (_opsCache.TryGet(InstanceId, out var cached))
                return cached!;

            try
            {
                string opsFilePath = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "ops.json");
                if (!File.Exists(opsFilePath))
                {
                    return [];
                }

                var ops = System.Text.Json.JsonSerializer.Deserialize<List<OpEntry>>(File.ReadAllText(opsFilePath));
                var result = ops?
                    .Where(op => !string.IsNullOrWhiteSpace(op.Name))
                    .OrderBy(op => op.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [];
                _opsCache.Set(InstanceId, result);
                return result;
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// 将管理员列表写入 ops.json。
        /// </summary>
        private void WriteOps(IEnumerable<OpEntry> ops)
        {
            string opsFilePath = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "ops.json");
            Directory.CreateDirectory(Path.GetDirectoryName(opsFilePath)!);
            File.WriteAllText(
                opsFilePath,
                System.Text.Json.JsonSerializer.Serialize(ops.OrderBy(op => op.Name, StringComparer.OrdinalIgnoreCase), options));
        }

        /// <summary>
        /// 重置在线玩家状态为初始状态。
        /// </summary>
        private void ResetOnlinePlayersState()
        {
            OnlinePlayersCount = 0;
            MaxPlayersCount = string.IsNullOrWhiteSpace(InstanceId)
                ? 20
                : ServerPropertiesManager.GetInt(InstanceId, "max-players", 20);
            OnlinePlayers.Clear();
            OnlinePlayersHint = "启动服务器以查看";
            IsRefreshingPlayers = false;
        }

        /// <summary>
        /// 启动服务器性能监控（CPU、内存）。
        /// </summary>
        private void StartPerformanceMonitoring()
        {
            try
            {
                _performanceMonitor?.Dispose();
                _performanceMonitor = new PerformanceMonitor(InstanceId);
                _performanceMonitor.DataUpdated += OnPerformanceDataUpdated;
                _performanceMonitor.Start();
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    StatusMessage = $"性能监控启动失败: {ex.Message}";
                });
            }
        }

        /// <summary>
        /// 排队执行服务器运行状态初始化。
        /// </summary>
        private void QueueRunningStateInitialization()
        {
            SafeFireAndForget(InitializeRunningStateAsync());
        }

        /// <summary>
        /// 异步初始化服务器运行状态（订阅输出、启动性能监控等）。
        /// </summary>
        private async Task InitializeRunningStateAsync()
        {
            try
            {
                SubscribeToProcessOutput();

                await Task.Run(StartPerformanceMonitoring);

                RunOnUiThread(() =>
                {
                    if (!IsRunning)
                    {
                        return;
                    }

                    LoadDashboardData();
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    StatusMessage = $"运行时初始化失败: {ex.Message}";
                });
            }
        }

        /// <summary>
        /// 性能数据更新回调，刷新 CPU 和内存数据到 UI。
        /// </summary>
        private void OnPerformanceDataUpdated(object? sender, PerformanceData data)
        {
            DispatcherHelper.InvokeIfNeeded(() =>
            {
                CpuUsage = data.CpuUsage;
                MemoryUsage = data.MemoryUsageMb;
                TotalStorageMb = data.TotalStorageMb;
                TotalStorage = FormatBytes(data.TotalStorageMb * 1024 * 1024);

                // 构建新的存储信息列表，仅在内容变化时替换
                var newWorldStorage = new List<WorldStorageInfo>();
                long maxSize = data.WorldStorageMb.Values.DefaultIfEmpty(1).Max();
                foreach (var kvp in data.WorldStorageMb)
                {
                    newWorldStorage.Add(new WorldStorageInfo
                    {
                        WorldName = kvp.Key switch
                        {
                            "world" => "主世界",
                            "world_nether" => "下界",
                            "world_the_end" => "末地",
                            _ => kvp.Key
                        },
                        SizeMb = kvp.Value,
                        SizeFormatted = FormatBytes(kvp.Value * 1024 * 1024),
                        SizePercent = maxSize > 0 ? (double)kvp.Value / maxSize * 100 : 0
                    });
                }

                if (newWorldStorage.Count != WorldStorageInfo.Count ||
                    !newWorldStorage.SequenceEqual(WorldStorageInfo, WorldStorageInfoComparer.Instance))
                {
                    WorldStorageInfo = new ObservableCollection<WorldStorageInfo>(newWorldStorage);
                }
            });
        }

        /// <summary>
        /// 停止性能监控并重置运行时数据。
        /// </summary>
        private void StopPerformanceMonitoring()
        {
            _performanceMonitor?.Dispose();
            _performanceMonitor = null;

            // 只重置运行时性能数据（CPU 和内存）
            // 存储信息保留，因为世界文件仍然存在
            CpuUsage = 0;
            MemoryUsage = 0;
        }

        /// <summary>
        /// 将字节数格式化为人类可读的大小文本。
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;
            const long TB = GB * 1024;

            return bytes switch
            {
                >= TB => $"{bytes / (double)TB:F2} TB",
                >= GB => $"{bytes / (double)GB:F2} GB",
                >= MB => $"{bytes / (double)MB:F2} MB",
                >= KB => $"{bytes / (double)KB:F2} KB",
                _ => $"{bytes} B"
            };
        }

        /// <summary>
        /// 加载实例的插件列表。
        /// </summary>
        private void LoadPlugins()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                var plugins = PluginManager.GetPlugins(InstanceId);
                RunOnUiThread(() =>
                {
                    Plugins.Clear();
                    foreach (var p in plugins)
                    {
                        Plugins.Add(new PluginDisplayItem(p));
                    }
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => StatusMessage = $"加载插件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载服务器属性配置（server.properties）。
        /// </summary>
        private void LoadServerProperties()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                var props = ServerPropertiesManager.Read(InstanceId);
                RunOnUiThread(() =>
                {
                    ServerProperties.Clear();
                    foreach (var kvp in props)
                    {
                        ServerProperties.Add(new ServerProperty(kvp.Key, kvp.Value));
                    }
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => StatusMessage = $"加载配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 启动服务器。
        /// </summary>
        [RelayCommand]
        private async Task StartServer()
        {
            if (IsRunning || IsStarting || InstanceInfo == null) return;

            try
            {
                IsStarting = true;

                // 检查是否已经在运行
                if (ServerProcessManager.IsRunning(InstanceId))
                {
                    IsRunning = true;
                    IsStarting = false;
                    StatusMessage = "服务器已在运行";
                    QueueRunningStateInitialization();
                    return;
                }

                // 清理控制台
                ClearConsole();
                StatusMessage = "正在启动服务器...";

                // 在后台线程执行启动操作，避免阻塞 UI
                var (process, success, errorMessage) = await Task.Run(async () =>
                {
                    try
                    {
                        var proc = new ServerProcess(InstanceId);

                        // 设置控制台输出事件
                        proc.OutputReceived += OnProcessOutputReceived;
                        proc.ErrorReceived += OnProcessErrorReceived;

                        // 启动进程（这个操作可能耗时）
                        await proc.StartAsync();

                        return (proc, true, (string?)null);
                    }
                    catch (Exception ex)
                    {
                        return ((ServerProcess?)null, false, ex.Message);
                    }
                });

                if (!success || process == null)
                {
                    StatusMessage = $"启动失败: {errorMessage}";
                    IsRunning = false;
                    return;
                }

                // 短暂等待检查是否快速退出
                await Task.Delay(500);

                if (!process.IsRunning)
                {
                    StatusMessage = "服务器启动失败，进程已退出";
                    try { process.Dispose(); } catch { }
                    IsRunning = false;
                    return;
                }

                // 注册到全局管理器（这会触发 InstanceStatusChanged 事件）
                ServerProcessManager.Register(InstanceId, process);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                StatusMessage = $"启动失败: {ex.Message}";
            }
            finally
            {
                IsStarting = false;
            }
        }

        /// <summary>
        /// 优雅停止服务器（等待进程退出）。
        /// </summary>
        [RelayCommand]
        private async Task StopServer()
        {
            if (!IsRunning) return;

            // 从 ServerProcessManager 获取进程
            var process = ServerProcessManager.GetProcess(InstanceId);
            if (process == null || !process.IsRunning)
            {
                IsRunning = false;
                StopPerformanceMonitoring();
                StatusMessage = "服务器未在运行";
                return;
            }

            try
            {
                ServerProcessManager.StopAndRemove(InstanceId);
                StatusMessage = "正在停止服务器...";

                // 等待进程退出（最多10秒）
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (!ServerProcessManager.IsRunning(InstanceId))
                    {
                        IsRunning = false;
                        StopPerformanceMonitoring();
                        StatusMessage = "服务器已停止";
                        return;
                    }
                }

                StatusMessage = "服务器停止超时，可以使用强制终止";
            }
            catch (Exception ex)
            {
                StatusMessage = $"停止失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 强制终止服务器进程。
        /// </summary>
        [RelayCommand]
        private void TerminateServer()
        {
            if (!IsRunning) return;

            // 检查进程是否真的在运行
            if (!ServerProcessManager.IsRunning(InstanceId))
            {
                IsRunning = false;
                StopPerformanceMonitoring();
                StatusMessage = "服务器未在运行";
                return;
            }

            try
            {
                ServerProcessManager.KillAndRemove(InstanceId);
                IsRunning = false;
                StopPerformanceMonitoring();
                StatusMessage = "服务器已被强制终止";
            }
            catch (Exception ex)
            {
                StatusMessage = $"终止失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 向服务器发送控制台命令。
        /// </summary>
        [RelayCommand]
        private void SendCommand()
        {
            if (string.IsNullOrWhiteSpace(CommandInput) || !IsRunning) return;

            var process = ServerProcessManager.GetProcess(InstanceId);
            if (process == null || !process.IsRunning)
            {
                StatusMessage = "服务器未在运行";
                IsRunning = false;
                return;
            }

            try
            {
                process.SendCommand(CommandInput);
                CommandInput = "";
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送命令失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 释放资源，停止监控并取消事件订阅。
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            StopPerformanceMonitoring();
            _consoleThrottler?.Dispose();
            _backupCts?.Dispose();
            ServerProcessManager.InstanceStatusChanged -= OnInstanceStatusChanged;
            OnlinePlayers.CollectionChanged -= OnOnlinePlayersCollectionChanged;
            AdminPlayers.CollectionChanged -= OnAdminPlayersCollectionChanged;
        }

        private void OnOnlinePlayersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ShowOnlinePlayersHint));
        }

        private void OnAdminPlayersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ShowAdminPlayersHint));
        }

        /// <summary>
        /// 打开文件选择对话框，选择自定义 JDK 可执行文件路径。
        /// </summary>
        [RelayCommand]
        private void SelectJdkPath()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Java 可执行文件 (java.exe)|java.exe|所有文件 (*.*)|*.*",
                Title = "选择 Java 可执行文件",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                EditJdkPath = dialog.FileName;
            }
        }

        /// <summary>
        /// 保存实例设置（内存、JDK、JVM 参数等）。
        /// </summary>
        [RelayCommand]
        private void SaveSettings()
        {
            if (InstanceInfo == null) return;

            try
            {
                if (!int.TryParse(EditMinMemory, out var minMem) || minMem < 512)
                {
                    StatusMessage = "最小内存不能小于 512 MB";
                    return;
                }

                if (!int.TryParse(EditMaxMemory, out var maxMem) || maxMem < minMem)
                {
                    StatusMessage = "最大内存不能小于最小内存";
                    return;
                }

                InstanceInfo.MinMemoryMb = minMem;
                InstanceInfo.MaxMemoryMb = maxMem;

                // JDK路径处理逻辑
                if (UseCustomJdk)
                {
                    if (string.IsNullOrWhiteSpace(EditJdkPath) || !File.Exists(EditJdkPath))
                    {
                        StatusMessage = "请选择有效的 java.exe 路径";
                        return;
                    }

                    // 使用自定义JDK路径
                    InstanceInfo.JdkPath = EditJdkPath;
                }
                else if (!AutoSelectJdk && SelectedInstalledJdk != null)
                {
                    // 手动选择管理器内的JDK
                    InstanceInfo.JdkPath = SelectedInstalledJdk.JavaExecutable;
                }
                else
                {
                    // 自动选择：根据Minecraft版本推荐JDK
                    InstanceInfo.JdkPath = "";
                }

                InstanceInfo.ExtraJvmArgs = EditExtraJvmArgs;

                InstanceInfo.ScheduledTaskList = _scheduledCommands.Split('\n',StringSplitOptions.TrimEntries);
                InstanceManager.UpdateInstance(InstanceInfo);
                SaveServerPropertiesInternal();
                InstanceManager.EnsureRconConfiguration(InstanceId);
                var props = ServerPropertiesManager.Read(InstanceId);
                UpdateServerAddressFromDict(props);
                LoadServerPropertiesFromDict(props);
                LoadPlayerManagementData();
                StatusMessage = "设置已保存";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 进入控制台全屏模式。
        /// </summary>
        [RelayCommand]
        private void EnterConsoleFullScreen() => IsConsoleFullScreen = true;

        /// <summary>
        /// 退出控制台全屏模式。
        /// </summary>
        [RelayCommand]
        private void ExitConsoleFullScreen() => IsConsoleFullScreen = false;

        /// <summary>
        /// 将界面中的服务器属性保存到 server.properties 文件。
        /// </summary>
        private void SaveServerPropertiesInternal()
        {
            if (string.IsNullOrWhiteSpace(InstanceId))
            {
                return;
            }

            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in ServerProperties)
            {
                if (string.IsNullOrWhiteSpace(property.Key))
                {
                    continue;
                }

                properties[property.Key.Trim()] = property.Value ?? string.Empty;
            }

            ServerPropertiesManager.WriteAll(InstanceId, properties);
        }

        /// <summary>
        /// 删除指定插件及其数据目录。
        /// </summary>
        [RelayCommand]
        private async Task DeletePlugin(PluginDisplayItem? plugin)
        {
            if (plugin == null || string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                // 显示确认对话框
                var dialog = new Wpf.Ui.Controls.ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要删除插件 \"{plugin.Name}\" 吗？\n\n此操作将删除插件文件及其数据目录！",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    DefaultButton = Wpf.Ui.Controls.ContentDialogButton.Close
                };

                var result = await _contentDialogService.ShowAsync(dialog, CancellationToken.None);

                if (result != Wpf.Ui.Controls.ContentDialogResult.Primary) return;

                // 先删除插件文件
                PluginManager.DeletePlugin(InstanceId, plugin.FileName);

                // 删除插件数据目录（如果存在）
                string pluginDataDir = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "plugins", plugin.Name);
                if (Directory.Exists(pluginDataDir))
                {
                    Directory.Delete(pluginDataDir, true); // 递归删除目录
                }

                // 在UI线程上更新插件列表
                RunOnUiThread(() =>
                {
                    Plugins.Remove(plugin);
                    StatusMessage = "插件已删除";
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    StatusMessage = $"删除失败: {ex.Message}";
                });
            }
        }

        /// <summary>
        /// 刷新插件列表。
        /// </summary>
        [RelayCommand]
        private void RefreshPlugins()
        {
            LoadPlugins();
        }

        /// <summary>
        /// 在资源管理器中打开指定插件的数据目录。
        /// </summary>
        [RelayCommand]
        private void OpenPluginDataFolder(PluginDisplayItem? plugin)
        {
            if (plugin == null || string.IsNullOrEmpty(plugin.FolderPath)) return;

            try
            {
                OpenFolder(plugin.FolderPath, "插件数据目录不存在");
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开目录失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 在资源管理器中打开插件目录。
        /// </summary>
        [RelayCommand]
        private void OpenPluginsFolder()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                string pluginsDir = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "plugins");
                OpenFolder(pluginsDir, "插件目录不存在");
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开插件目录失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 在资源管理器中打开实例目录。
        /// </summary>
        [RelayCommand]
        private void OpenInstanceFolder()
        {
            if (string.IsNullOrWhiteSpace(InstanceId))
            {
                return;
            }

            try
            {
                OpenFolder(PathHelper.GetInstanceDir(InstanceId), "实例目录不存在");
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开实例目录失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 打开指定文件夹，若不存在则显示提示消息。
        /// </summary>
        private void OpenFolder(string path, string missingMessage)
        {
            if (!Directory.Exists(path))
            {
                StatusMessage = missingMessage;
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }

        /// <summary>
        /// 切换插件的启用/禁用状态（通过重命名 .jar 文件）。
        /// </summary>
        [RelayCommand]
        private void TogglePluginEnabled(PluginDisplayItem? plugin)
        {
            if (plugin == null || string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                string pluginsDir = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "plugins");

                // 根据插件当前状态决定操作
                if (plugin.IsEnabled)
                {
                    // 当前是启用状态，需要禁用
                    string pluginFilePath = Path.Combine(pluginsDir, plugin.FileName);
                    string disabledFilePath = Path.ChangeExtension(pluginFilePath, ".jar.dis");

                    if (File.Exists(pluginFilePath))
                    {
                        if (!File.Exists(disabledFilePath))
                        {
                            File.Move(pluginFilePath, disabledFilePath);
                            plugin.IsEnabled = false;
                            StatusMessage = "插件已禁用";
                        }
                        else
                        {
                            StatusMessage = "禁用文件已存在";
                        }
                    }
                    else
                    {
                        StatusMessage = "插件文件不存在";
                    }
                }
                else
                {
                    // 当前是禁用状态，需要启用
                    string pluginFileNameWithoutDis = plugin.FileName.EndsWith(".dis")
                        ? plugin.FileName[..^4] // 移除 .dis
                        : plugin.FileName;

                    string disabledFilePath = Path.Combine(pluginsDir, plugin.FileName);
                    string enabledFilePath = Path.Combine(pluginsDir, pluginFileNameWithoutDis);

                    if (File.Exists(disabledFilePath))
                    {
                        // 需要重命名为启用状态的文件名
                        string targetFileName = Path.GetFileNameWithoutExtension(pluginFileNameWithoutDis) + ".jar";
                        string targetPath = Path.Combine(pluginsDir, targetFileName);

                        if (!File.Exists(targetPath))
                        {
                            File.Move(disabledFilePath, targetPath);
                            plugin.IsEnabled = true;
                            StatusMessage = "插件已启用";
                        }
                        else
                        {
                            StatusMessage = "启用文件已存在";
                        }
                    }
                    else
                    {
                        StatusMessage = "禁用的插件文件不存在";
                    }
                }

                // 重新加载插件列表以反映更改
                LoadPlugins();
            }
            catch (Exception ex)
            {
                StatusMessage = $"切换插件状态失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 加载仪表盘相关数据（游戏模式、在线模式、服务器地址等）。
        /// </summary>
        public void LoadDashboardData()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            var props = ServerPropertiesManager.Read(InstanceId);

            LoadServerPropertiesFromDict(props);
            UpdateServerAddressFromDict(props);

            if (IsRunning)
            {
                StartServerStatusPolling();
                StartUptimeCounter();
            }
            else
            {
                ResetOnlinePlayersState();
            }
        }

        private void LoadServerPropertiesFromDict(Dictionary<string, string> props)
        {
            try
            {
                var gameModeValue = props.GetValueOrDefault("gamemode", "survival");
                GameMode = gameModeValue switch
                {
                    "survival" => "生存模式",
                    "creative" => "创造模式",
                    "adventure" => "冒险模式",
                    "spectator" => "旁观模式",
                    _ => gameModeValue
                };

                IsOnlineMode = props.GetValueOrDefault("online-mode", "true").Equals("true", StringComparison.CurrentCultureIgnoreCase);
                SimulationDistance = int.TryParse(props.GetValueOrDefault("simulation-distance", "0"), out var simDist) ? simDist : 0;
                ViewDistance = int.TryParse(props.GetValueOrDefault("view-distance", "0"), out var viewDist) ? viewDist : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载服务器属性失败: {ex.Message}");
            }
        }

        private void UpdateServerAddressFromDict(Dictionary<string, string> props)
        {
            try
            {
                var ip = props.GetValueOrDefault("server-ip", "");
                var port = props.GetValueOrDefault("server-port", "25565");

                if (string.IsNullOrWhiteSpace(ip))
                    ServerAddress = $"localhost:{port}";
                else
                    ServerAddress = $"{ip}:{port}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新服务器地址失败: {ex.Message}");
                ServerAddress = "localhost:25565";
            }
        }

        /// <summary>
        /// 通过 RCON 刷新在线玩家列表。
        /// </summary>
        private async Task RefreshOnlinePlayersAsync()
        {
            if (!await _playerRefreshLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                RunOnUiThread(() =>
                {
                    IsRefreshingPlayers = true;
                    OnlinePlayersHint = "正在读取在线玩家...";
                });

                var process = ServerProcessManager.GetProcess(InstanceId);
                if (process == null || !process.IsRunning)
                {
                    RunOnUiThread(ResetOnlinePlayersState);
                    return;
                }

                string response = await process.ExecuteRconCommandAsync("list");
                var onlinePlayersState = ParseOnlinePlayersResponse(response);
                var opLookup = ReadOps()
                    .Select(op => op.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                int configuredMaxPlayers = ServerPropertiesManager.GetInt(InstanceId, "max-players", 20);

                RunOnUiThread(() =>
                {
                    OnlinePlayersCount = onlinePlayersState.OnlineCount;
                    MaxPlayersCount = onlinePlayersState.MaxPlayers > 0 ? onlinePlayersState.MaxPlayers : configuredMaxPlayers;
                    OnlinePlayers.Clear();

                    foreach (string playerName in onlinePlayersState.PlayerNames)
                    {
                        OnlinePlayers.Add(new PlayerDisplayItem(playerName, string.Empty)
                        {
                            IsOp = opLookup.Contains(playerName)
                        });
                    }

                    OnlinePlayersHint = OnlinePlayers.Count == 0 ? "当前没有在线玩家" : string.Empty;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取在线玩家失败: {ex.Message}");
                RunOnUiThread(() =>
                {
                    OnlinePlayersCount = 0;
                    MaxPlayersCount = ServerPropertiesManager.GetInt(InstanceId, "max-players", 20);
                    OnlinePlayers.Clear();
                    OnlinePlayersHint = IsRunning ? "服务器正在启动或 RCON 尚未就绪" : "启动服务器以查看";
                });
            }
            finally
            {
                RunOnUiThread(() => IsRefreshingPlayers = false);
                _playerRefreshLock.Release();
            }
        }

        /// <summary>
        /// 解析 RCON list 命令的响应，提取在线玩家数和玩家名。
        /// </summary>
        private static OnlinePlayersState ParseOnlinePlayersResponse(string response)
        {
            string normalized = (response ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            var numbers = ParseOnlinePlayersResponse().Matches(normalized)
                .Select(match => int.TryParse(match.Value, out int value) ? value : 0)
                .ToList();

            int onlineCount = numbers.Count > 0 ? numbers[0] : 0;
            int maxPlayers = numbers.Count > 1 ? numbers[1] : 0;

            int separatorIndex = Math.Max(normalized.LastIndexOf(':'), normalized.LastIndexOf('：'));
            var playerNames = new List<string>();
            if (separatorIndex >= 0 && separatorIndex < normalized.Length - 1)
            {
                playerNames = [.. normalized[(separatorIndex + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)];
            }

            return new OnlinePlayersState(onlineCount, maxPlayers, playerNames);
        }

        /// <summary>
        /// 检查指定玩家是否为管理员。
        /// </summary>
        private bool IsPlayerOp(string playerName)
        {
            return ReadOps().Any(op => string.Equals(op.Name, playerName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 启动服务器状态轮询定时器（每 5 秒刷新在线玩家）。
        /// </summary>
        private void StartServerStatusPolling()
        {
            _serverStatusTimer?.Dispose();
            _serverStatusTimer = new Timer(_ =>
            {
                SafeFireAndForget(RefreshOnlinePlayersAsync());
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// 启动服务器运行时长计数器（每秒更新）。
        /// </summary>
        private void StartUptimeCounter()
        {
            _serverStartTime = ServerProcessManager.GetStartTime(InstanceId) ?? DateTime.Now;
            _uptimeTimer?.Stop();
            _uptimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uptimeTimer.Tick += (_, _) =>
            {
                var uptime = DateTime.Now - _serverStartTime;
                Uptime = $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
            };
            _uptimeTimer.Start();
        }

        /// <summary>
        /// 启动仪表盘的性能监控（CPU 和内存）。
        /// </summary>

        /// <summary>
        /// 将服务器地址复制到剪贴板。
        /// </summary>
        [RelayCommand]
        private void CopyServerAddress()
        {
            Clipboard.SetText(ServerAddress);
            StatusMessage = "服务器地址已复制到剪贴板";
        }

        /// <summary>
        /// 将指定玩家设为管理员（OP）。
        /// </summary>
        [RelayCommand]
        private async Task SetPlayerAsOp(PlayerDisplayItem? player)
        {
            if (player == null || string.IsNullOrEmpty(InstanceId))
            {
                return;
            }

            try
            {
                if (IsPlayerOp(player.Name))
                {
                    StatusMessage = $"{player.Name} 已经是管理员";
                    return;
                }

                if (IsRunning)
                {
                    await ExecutePlayerRconCommandAsync($"op {player.Name}");
                    await Task.Delay(300);
                }
                else
                {
                    var ops = ReadOps();
                    ops.Add(new OpEntry
                    {
                        Name = player.Name,
                        Uuid = player.Id,
                        Level = 4,
                        BypassesPlayerLimit = false
                    });
                    WriteOps(ops);
                }

                player.IsOp = true;
                LoadAdminPlayers();
                await RefreshOnlinePlayersAsync();
                StatusMessage = $"已将 {player.Name} 设为管理员";
            }
            catch (Exception ex)
            {
                StatusMessage = $"设置管理员失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 移除指定玩家的管理员权限。
        /// </summary>
        [RelayCommand]
        private async Task RemovePlayerOp(PlayerDisplayItem? player)
        {
            if (player == null || string.IsNullOrEmpty(InstanceId))
            {
                return;
            }

            try
            {
                if (!IsPlayerOp(player.Name))
                {
                    StatusMessage = $"{player.Name} 不是管理员";
                    return;
                }

                if (IsRunning)
                {
                    await ExecutePlayerRconCommandAsync($"deop {player.Name}");
                    await Task.Delay(300);
                }
                else
                {
                    var ops = ReadOps();
                    var opToRemove = ops.FirstOrDefault(op => string.Equals(op.Name, player.Name, StringComparison.OrdinalIgnoreCase));
                    if (opToRemove != null)
                    {
                        ops.Remove(opToRemove);
                        WriteOps(ops);
                    }
                }

                player.IsOp = false;
                LoadAdminPlayers();
                await RefreshOnlinePlayersAsync();
                StatusMessage = $"已移除 {player.Name} 的管理员权限";
            }
            catch (Exception ex)
            {
                StatusMessage = $"取消管理员失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 踢出指定在线玩家。
        /// </summary>
        [RelayCommand]
        private async Task KickPlayer(PlayerDisplayItem? player)
        {
            if (player == null)
            {
                return;
            }

            if (!SecurityHelper.IsValidPlayerName(player.Name))
            {
                StatusMessage = "无效的玩家名称";
                return;
            }

            try
            {
                await ExecutePlayerRconCommandAsync($"kick {player.Name}");
                await Task.Delay(200);
                await RefreshOnlinePlayersAsync();
                StatusMessage = $"已踢出玩家 {player.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"踢出玩家失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 封禁指定玩家。
        /// </summary>
        [RelayCommand]
        private async Task BanPlayer(PlayerDisplayItem? player)
        {
            if (player == null)
            {
                return;
            }

            if (!SecurityHelper.IsValidPlayerName(player.Name))
            {
                StatusMessage = "无效的玩家名称";
                return;
            }

            try
            {
                await ExecutePlayerRconCommandAsync($"ban {player.Name}");
                await Task.Delay(200);
                await RefreshOnlinePlayersAsync();
                StatusMessage = $"已封禁玩家 {player.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"封禁玩家失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 封禁指定玩家的 IP 地址。
        /// </summary>
        [RelayCommand]
        private async Task BanPlayerIp(PlayerDisplayItem? player)
        {
            if (player == null)
            {
                return;
            }

            if (!SecurityHelper.IsValidPlayerName(player.Name))
            {
                StatusMessage = "无效的玩家名称";
                return;
            }

            try
            {
                await ExecutePlayerRconCommandAsync($"ban-ip {player.Name}");
                await Task.Delay(200);
                await RefreshOnlinePlayersAsync();
                StatusMessage = $"已封禁 {player.Name} 的 IP";
            }
            catch (Exception ex)
            {
                StatusMessage = $"封禁 IP 失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 向指定玩家发送私聊消息。
        /// </summary>
        [RelayCommand]
        private async Task SendMessageToPlayer(PlayerDisplayItem? player)
        {
            if (player == null)
            {
                return;
            }

            if (!SecurityHelper.IsValidPlayerName(player.Name))
            {
                StatusMessage = "无效的玩家名称";
                return;
            }

            var messageBox = new Wpf.Ui.Controls.TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinWidth = 360,
                MinHeight = 120,
                PlaceholderText = "输入要发送的消息"
            };

            var dialogContent = new StackPanel
            {
                Children =
                {
                    new System.Windows.Controls.TextBlock
                    {
                        Text = $"发送给 {player.Name}",
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    messageBox
                }
            };

            var dialog = new Wpf.Ui.Controls.ContentDialog
            {
                Title = "发送消息",
                Content = dialogContent,
                PrimaryButtonText = "发送",
                CloseButtonText = "取消",
                DefaultButton = Wpf.Ui.Controls.ContentDialogButton.Primary
            };

            var result = await _contentDialogService.ShowAsync(dialog, CancellationToken.None);
            string message = messageBox.Text?.Trim() ?? string.Empty;
            if (result != Wpf.Ui.Controls.ContentDialogResult.Primary || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                string singleLineMessage = SingleLineMessage().Replace(message, " ").Trim();
                await ExecutePlayerRconCommandAsync($"msg {player.Name} {singleLineMessage}");
                StatusMessage = $"已向 {player.Name} 发送消息";
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送消息失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 刷新玩家管理数据。
        /// </summary>
        [RelayCommand]
        private void RefreshPlayers()
        {
            LoadPlayerManagementData();
            StatusMessage = "玩家信息已刷新";
        }

        /// <summary>
        /// 通过 RCON 执行玩家管理相关命令。
        /// </summary>
        private async Task ExecutePlayerRconCommandAsync(string command)
        {
            var process = ServerProcessManager.GetProcess(InstanceId);
            if (process == null || !process.IsRunning)
            {
                throw new InvalidOperationException("服务器未运行，无法执行该操作。");
            }

            await process.ExecuteRconCommandAsync(command);
        }

        /// <summary>
        /// 刷新仪表盘数据。
        /// </summary>
        [RelayCommand]
        private void RefreshDashboard()
        {
            LoadDashboardData();
            StatusMessage = "仪表盘数据已刷新";
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex SingleLineMessage();
        [GeneratedRegex(@"\d+")]
        private static partial Regex ParseOnlinePlayersResponse();
    }

    /// <summary>
    /// 在线玩家显示项
    /// </summary>
    public partial class PlayerDisplayItem(string name, string id) : ObservableObject
    {
        private string _name = name;
        private string _id = id;
        private string _secondaryText = "";

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Id
        {
            get => _id;
            set
            {
                if (SetProperty(ref _id, value))
                {
                    OnPropertyChanged(nameof(HasId));
                }
            }
        }

        public bool HasId => !string.IsNullOrWhiteSpace(Id);

        public string SecondaryText
        {
            get => _secondaryText;
            set
            {
                if (SetProperty(ref _secondaryText, value))
                {
                    OnPropertyChanged(nameof(HasSecondaryText));
                }
            }
        }

        public bool HasSecondaryText => !string.IsNullOrWhiteSpace(SecondaryText);

        private bool _isOp;
        public bool IsOp
        {
            get => _isOp;
            set
            {
                if (_isOp != value)
                {
                    _isOp = value;
                    OnPropertyChanged();
                }
            }
        }
    }

    /// <summary>
    /// ops.json 中的管理员条目。
    /// </summary>
    public class OpEntry
    {
        /// <summary>玩家名称。</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>玩家 UUID。</summary>
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = "";

        /// <summary>OP 权限等级。</summary>
        [JsonPropertyName("level")]
        public int Level { get; set; } = 4;

        /// <summary>是否绕过玩家上限。</summary>
        [JsonPropertyName("bypassesPlayerLimit")]
        public bool BypassesPlayerLimit { get; set; } = false;
    }

    /// <summary>
    /// 在线玩家状态快照，包含在线数、最大玩家数和玩家名列表。
    /// </summary>
    internal readonly record struct OnlinePlayersState(int OnlineCount, int MaxPlayers, IReadOnlyList<string> PlayerNames);

    /// <summary>
    /// 插件显示项
    /// </summary>
    public partial class PluginDisplayItem(PluginInfo info) : ObservableObject
    {
        public string Name { get; } = info.Name;
        public string Version { get; } = info.Version;
        public string Description { get; } = info.Description;
        public string FileName { get; } = info.FileName;
        public string Authors { get; } = string.Join(", ", info.Authors);
        public string FolderPath { get; } = string.IsNullOrEmpty(info.FilePath) ? "" :
            Path.Combine(Path.GetDirectoryName(info.FilePath) ?? "", info.Name); // 指向插件数据目录
        public bool IsEnabled { get; set; } = !info.IsDisabled; // 根据插件信息设置启用状态
    }

    /// <summary>
    /// 世界文件夹存储信息。
    /// </summary>
    public class WorldStorageInfo
    {
        public string WorldName { get; set; } = "";
        public long SizeMb { get; set; }
        public string SizeFormatted { get; set; } = "";
        public double SizePercent { get; set; }
    }

    internal sealed class WorldStorageInfoComparer : IEqualityComparer<WorldStorageInfo>
    {
        public static readonly WorldStorageInfoComparer Instance = new();

        public bool Equals(WorldStorageInfo? x, WorldStorageInfo? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.WorldName == y.WorldName &&
                   x.SizeMb == y.SizeMb &&
                   x.SizeFormatted == y.SizeFormatted &&
                   x.SizePercent.Equals(y.SizePercent);
        }

        public int GetHashCode(WorldStorageInfo obj)
        {
            return HashCode.Combine(obj.WorldName, obj.SizeMb, obj.SizeFormatted, obj.SizePercent);
        }
    }
    /// <summary>
    /// 带进度回调的包装流，用于在读取数据时报告进度。
    /// </summary>
    public class ProgressStream(Stream innerStream, long totalBytes, Action<long> reportProgress) : Stream
    {
        /// <summary>内部包装的流。</summary>
        private readonly Stream _innerStream = innerStream;

        /// <summary>进度报告回调。</summary>
        private readonly Action<long> _reportProgress = reportProgress;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead > 0) _reportProgress?.Invoke(bytesRead);
            return bytesRead;
        }

        // 保留旧版数组重载以兼容老代码
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            if (bytesRead > 0) _reportProgress?.Invoke(bytesRead);
            return bytesRead;
        }

        // ✅ 新增：同步的 Span 版本也应一并重写
        public override int Read(Span<byte> buffer)
        {
            int bytesRead = _innerStream.Read(buffer);
            if (bytesRead > 0) _reportProgress?.Invoke(bytesRead);
            return bytesRead;
        }

        // 保留旧版同步数组重载
        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _innerStream.Read(buffer, offset, count);
            if (bytesRead > 0) _reportProgress?.Invoke(bytesRead);
            return bytesRead;
        }

        // 其他 Stream 必须重写的成员，直接转发
        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }
        public override void Flush() => _innerStream.Flush();
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _innerStream.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            await _innerStream.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}

