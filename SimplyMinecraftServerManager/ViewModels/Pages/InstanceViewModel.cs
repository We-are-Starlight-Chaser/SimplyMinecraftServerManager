// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Microsoft.Win32;
using SharpCompress.Common;
using SharpCompress.Writers.Tar;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Helpers;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Models;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.Views.Pages;
using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
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
    /// 服务器实例详情页面的视图模型，管理实例的启动/停止、控制台、插件/模组、配置、玩家管理和仪表盘等功能。
    /// </summary>
    public partial class InstanceViewModel : ObservableObject, INavigationAware, IDisposable
    {
        /// <summary>JSON 序列化选项，用于格式化输出。</summary>
        private readonly System.Text.Json.JsonSerializerOptions options = new() { WriteIndented = true };

        /// <summary>内容对话框服务。</summary>
        private readonly IContentDialogService _contentDialogService;
        private readonly INavigationService _navigationService;
        private readonly NavigationParameterService _navigationParameterService;
        private readonly AppNotificationService _notificationService;
        private PerformanceMonitor? _performanceMonitor;
        private readonly SemaphoreSlim _playerRefreshLock = new(1, 1);
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

        /// <summary>是否为模组服务端（Fabric/Forge/NeoForge/Quilt）。</summary>
        public bool IsModServer => ServerType.Equals("fabric", StringComparison.OrdinalIgnoreCase)
            || ServerType.Equals("forge", StringComparison.OrdinalIgnoreCase)
            || ServerType.Equals("neoforge", StringComparison.OrdinalIgnoreCase)
            || ServerType.Equals("neoforged", StringComparison.OrdinalIgnoreCase)
            || ServerType.Equals("quilt", StringComparison.OrdinalIgnoreCase);

        /// <summary>插件/模组管理标签页标题。</summary>
        public string PluginModTabHeader => IsModServer ? "模组管理" : "插件管理";

        /// <summary>MOD服必须备份的目录和文件清单。</summary>
        private static readonly string[] ModServerMustBackup = [
            "mods",
            "config",
            "server.properties",
            "eula.txt",
            "ops.json",
            "whitelist.json",
            "banned-players.json",
            "banned-ips.json",
        ];

        /// <summary>插件服必须备份的目录和文件清单。</summary>
        private static readonly string[] PluginServerMustBackup = [
            "plugins",
            "config",
            "server.properties",
            "eula.txt",
            "ops.json",
            "whitelist.json",
            "banned-players.json",
            "banned-ips.json",
        ];

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

        /// <summary>页面导航进入时触发的事件，用于重置滚动位置等 UI 状态。</summary>
        public event EventHandler? NavigatedTo;

        /// <summary>控制台最大行数。</summary>
        public static int MaxConsoleLines => 1000;

        /// <summary>控制台命令输入文本。</summary>
        [ObservableProperty]
        public partial string CommandInput { get; set; } = "";

        /// <summary>插件/模组列表。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PluginModsCount))]
        public partial ObservableCollection<PluginModDisplayItem> PluginMods { get; set; } = [];
        public int PluginModsCount => PluginMods.Count;

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

        private static readonly MemoryCache<List<OpEntry>> _opsCache = new(TimeSpan.FromSeconds(60), 50);
        private Dictionary<string, string>? _cachedServerProps;
        private string? _cachedServerPropsInstanceId;

        /// <summary>
        /// 初始化实例详情视图模型。
        /// </summary>
        /// <param name="contentDialogService">内容对话框服务。</param>
        /// <param name="navigationService">导航服务。</param>
        /// <param name="navigationParameterService">导航参数服务。</param>
        public InstanceViewModel(
            IContentDialogService contentDialogService,
            INavigationService navigationService,
            NavigationParameterService navigationParameterService,
            AppNotificationService notificationService)
        {
            _contentDialogService = contentDialogService;
            _navigationService = navigationService;
            _navigationParameterService = navigationParameterService;
            _notificationService = notificationService;
            LoadConsolePreferences();
            PluginMods.CollectionChanged += OnPluginModsCollectionChanged;
            OnlinePlayers.CollectionChanged += OnOnlinePlayersCollectionChanged;
            AdminPlayers.CollectionChanged += OnAdminPlayersCollectionChanged;

            // 订阅全局状态变化事件
            ServerProcessManager.InstanceStatusChanged += OnInstanceStatusChanged;
        }

        /// <summary>
        /// 在资源管理器中打开备份文件夹
        /// </summary>
        [RelayCommand]
        private void OpenBackupsFolder()
        {
            if (string.IsNullOrWhiteSpace(InstanceId))
            {
                return;
            }

            try
            {
                OpenFolder(PathHelper.GetBackupsDir(InstanceId), "备份目录不存在");
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开备份目录失败: {ex.Message}";
            }
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
            bool saveOffExecuted = false;

            try
            {
                BackupProgress = 0;

                if (ServerProcessManager.IsRunning(InstanceId))
                {
                    try
                    {
                        await ExecutePlayerRconCommandAsync("save-off");
                        saveOffExecuted = true;
                        await ExecutePlayerRconCommandAsync("save-all flush");
                        await Task.Delay(2500, token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        var res = await _contentDialogService.ShowAsync(new ContentDialog()
                        {
                            Title = "SMSM",
                            Content = $"无法强制保存服务器文件！\n{ex.Message}\n\n您是否要继续？（可能会损坏存档）",
                            PrimaryButtonAppearance = ControlAppearance.Danger,
                            PrimaryButtonText = "继续备份",
                            CloseButtonText = "取消",
                            CloseButtonAppearance = ControlAppearance.Secondary,
                        }, token);

                        if (res != ContentDialogResult.Primary) return;
                    }
                }

                string serverRoot = PathHelper.GetInstanceDir(InstanceId);
                string destDir = PathHelper.GetBackupsDir(InstanceId);
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                string backupFile = Path.Combine(destDir, $"{DateTimeOffset.Now:yyyy_MM_dd_HH_mm_ss}.tar.zst");

                await CreateTarZstdWithProgress(serverRoot, backupFile, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _notificationService.ShowInfo("备份", "备份操作已取消");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Backup] 备份失败: {ex}");
                _notificationService.ShowDanger("备份", $"备份出现异常: {ex.Message}");
            }
            finally
            {
                if (saveOffExecuted && ServerProcessManager.IsRunning(InstanceId))
                {
                    try
                    {
                        await ExecutePlayerRconCommandAsync("save-on");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Backup] 恢复 save-on 失败: {ex.Message}");
                        _notificationService.ShowDanger("备份", "⚠️ 恢复 save-on 失败，请手动执行！");
                    }
                }

                // ✅ 仅在非取消状态下报告成功
                if (!token.IsCancellationRequested)
                {
                    BackupProgress = 100;
                    _notificationService.ShowSuccess("备份", "备份已完成");
                }

                // ✅ 安全释放 CTS
                if (Interlocked.CompareExchange(ref _backupCts, null, currentCts) == currentCts)
                    currentCts.Dispose();
            }
        }

        public async Task CreateTarZstdWithProgress(string serverRoot, string outputFilePath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

            var (Directories, Files) = CollectBackupTargets(serverRoot);
            if (Directories.Count == 0 && Files.Count == 0)
            {
                Debug.WriteLine("[Backup] ⚠️ 未找到任何需要备份的内容");
                return;
            }

            long totalSize = 0;
            var fileEntries = new List<(string Path, string Relative, long Length)>();

            foreach (var dir in Directories)
            {
                if (!Directory.Exists(dir)) continue;
                var srcDir = new DirectoryInfo(dir);
                string relativeBase = Path.GetRelativePath(serverRoot, dir).Replace('\\', '/');

                foreach (var fi in srcDir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    ReadOnlySpan<char> fullPath = fi.FullName.AsSpan();
                    ReadOnlySpan<char> name = fi.Name.AsSpan();

                    if (fullPath.Contains("\\logs\\", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("session.lock", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".pid", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("~$", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (fi.Length == 0) continue;

                    totalSize += fi.Length;
                    string relPath = $"{relativeBase}/{Path.GetRelativePath(dir, fi.FullName).Replace('\\', '/')}";
                    fileEntries.Add((fi.FullName, relPath, fi.Length));
                }
            }

            // 单独添加的根级文件
            foreach (var filePath in Files)
            {
                if (!File.Exists(filePath)) continue;
                var fi = new FileInfo(filePath);
                if (fi.Length == 0) continue;

                totalSize += fi.Length;
                string relPath = Path.GetRelativePath(serverRoot, filePath).Replace('\\', '/');
                fileEntries.Add((filePath, relPath, fi.Length));
            }

            if (fileEntries.Count == 0) return;

            // ✅ 3. Channel 流水线备份（与之前相同的高性能管线）
            long processedSize = 0;
            const int ioBufferSize = 81920;
            const int maxOpenRetries = 3;
            const int retryBaseDelayMs = 100;

            int concurrency = Math.Min(Environment.ProcessorCount * 2, 16);
            var channel = System.Threading.Channels.Channel.CreateBounded<(string Relative, byte[] Data, long Size)>(
                new System.Threading.Channels.BoundedChannelOptions(concurrency)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false
                });

            var producerTask = Task.Run(async () =>
            {
                try
                {
                    foreach (var (filePath, relativePath, fileSize) in fileEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        FileStream? fs = null;
                        for (int attempt = 0; attempt <= maxOpenRetries; attempt++)
                        {
                            try
                            {
                                fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                    FileShare.ReadWrite | FileShare.Delete,
                                    ioBufferSize, FileOptions.SequentialScan);
                                break;
                            }
                            catch (IOException) when (attempt < maxOpenRetries)
                            {
                                await Task.Delay(retryBaseDelayMs * (attempt + 1), cancellationToken);
                            }
                            catch (UnauthorizedAccessException) when (attempt < maxOpenRetries)
                            {
                                await Task.Delay(retryBaseDelayMs * (attempt + 1), cancellationToken);
                            }
                        }

                        if (fs is null)
                        {
                            Debug.WriteLine($"[Backup] ⚠️ 跳过无法打开的文件: {relativePath}");
                            Interlocked.Add(ref processedSize, fileSize);
                            continue;
                        }

                        await using (fs)
                        {
                            var buffer = new byte[fileSize];
                            int bytesRead = 0;
                            while (bytesRead < fileSize)
                            {
                                int read = await fs.ReadAsync(buffer.AsMemory(bytesRead), cancellationToken);
                                if (read == 0) break;
                                bytesRead += read;
                            }
                            await channel.Writer.WriteAsync((relativePath, buffer, fileSize), cancellationToken);
                        }
                    }
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);

            using var fileStream = new FileStream(
                outputFilePath, FileMode.Create, FileAccess.Write,
                FileShare.None, ioBufferSize,
                FileOptions.SequentialScan | FileOptions.Asynchronous);

            using var zstdStream = new CompressionStream(fileStream, new CompressionOptions(3));

            var writerOptions = new TarWriterOptions(CompressionType.None)
            {
                ArchiveEncoding = new ArchiveEncoding { Default = System.Text.Encoding.UTF8 },
            };

            using var tarWriter = new TarWriter(zstdStream, writerOptions);

            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            {
                using var dataStream = new MemoryStream(entry.Data, writable: false);
                using var progressStream = new ProgressStream(dataStream, entry.Size, bytesRead =>
                {
                    long newTotal = Interlocked.Add(ref processedSize, bytesRead);
                    BackupProgress = Math.Min(100.0, (double)newTotal / totalSize * 100);
                });

                await tarWriter.WriteAsync(
                    entry.Relative, progressStream,
                    DateTimeOffset.UtcNow.UtcDateTime,
                    cancellationToken: cancellationToken);
            }

            await producerTask;
        }

        /// <summary>
        /// 智能收集备份目标：世界目录 + mods/plugins + 配置文件 + jar文件
        /// </summary>
        private (List<string> Directories, List<string> Files) CollectBackupTargets(string serverRoot)
        {
            var directories = new List<string>(8);
            var files = new List<string>(16);
            var worldDirs = GetWorldDirectories();
            foreach (var worldDir in worldDirs)
            {
                string fullPath = Path.Combine(serverRoot, worldDir);
                if (Directory.Exists(fullPath))
                    directories.Add(fullPath);
            }
            if (IsModServer)
            {
                string modsDir = Path.Combine(serverRoot, "mods");
                if (Directory.Exists(modsDir)) directories.Add(modsDir);

                // 模组服常见配置目录
                string[] modConfigDirs = ["config", "defaultconfigs", "kubejs", "scripts"];
                foreach (var cfgDir in modConfigDirs)
                {
                    string p = Path.Combine(serverRoot, cfgDir);
                    if (Directory.Exists(p)) directories.Add(p);
                }
            }
            else
            {
                string pluginsDir = Path.Combine(serverRoot, "plugins");
                if (Directory.Exists(pluginsDir)) directories.Add(pluginsDir);
            }
            string[] commonDirs = ["datapacks", "resourcepacks", "behavior_packs"];
            foreach (var d in commonDirs)
            {
                string p = Path.Combine(serverRoot, d);
                if (Directory.Exists(p)) directories.Add(p);
            }
            string[] configFiles =
            [
        "server.properties", "spigot.yml", "paper.yml", "bukkit.yml",
        "velocity.toml", "config.toml", "ops.json", "whitelist.json",
        "banned-players.json", "banned-ips.json", "usercache.json",
        "eula.txt"
    ];
            foreach (var f in configFiles)
            {
                string p = Path.Combine(serverRoot, f);
                if (File.Exists(p)) files.Add(p);
            }

            // ✅ Jar 文件（服务端核心）
            try
            {
                foreach (var jar in Directory.EnumerateFiles(serverRoot, "*.jar", SearchOption.TopDirectoryOnly))
                {
                    files.Add(jar);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Backup] 枚举jar文件失败: {ex.Message}");
            }

            return (directories, files);
        }

        private List<string> GetWorldDirectories()
        {
            var worldDirs = new List<string>(3);
            try
            {
                var props = ServerPropertiesManager.Read(InstanceId);
                string levelName = props.GetValueOrDefault("level-name", "world");
                if (string.IsNullOrWhiteSpace(levelName)) levelName = "world";

                worldDirs.Add(levelName);
                if (!IsModServer)
                {
                    worldDirs.Add($"{levelName}_nether");
                    worldDirs.Add($"{levelName}_the_end");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Backup] 读取服务器属性失败，回退默认世界目录: {ex.Message}");
                worldDirs.Add("world");
                if (!IsModServer)
                {
                    worldDirs.Add("world_nether");
                    worldDirs.Add("world_the_end");
                }
            }
            return worldDirs;
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
            _notificationService.ShowSuccess("控制台", "控制台内容已复制到剪贴板");
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
            NavigatedTo?.Invoke(this, EventArgs.Empty);
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
            StopServerStatusPolling();
            StopUptimeCounter();
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
        /// 异步加载实例数据，包括基本信息、插件/模组、配置和性能数据。
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

            // 在后台线程读取 JAR 元数据和 JDK 列表，避免阻塞 UI
            var metadata = await Task.Run(() => ServerJarMetadataReader.Read(info));
            ServerType = metadata.ServerType;
            MinecraftVersion = metadata.MinecraftVersion;
            EditMinMemory = info.MinMemoryMb.ToString();
            EditMaxMemory = info.MaxMemoryMb.ToString();
            EditJdkPath = info.JdkPath;
            EditExtraJvmArgs = info.ExtraJvmArgs;
            LoadConsolePreferences();

            var installedJdks = await Task.Run(() => JdkManager.GetInstalledJdks());
            InstalledJdks = new ObservableCollection<InstalledJdk>(installedJdks);
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
                try { LoadPluginMods(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadPluginMods failed: {ex.Message}"); }
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
        /// 加载实例的静态存储空间信息（异步执行，避免 UI 冻结）。
        /// </summary>
        private void LoadStaticStorageInfo()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            SafeFireAndForget(LoadStaticStorageInfoAsync());
        }

        /// <summary>
        /// 异步加载实例的静态存储空间信息。
        /// </summary>
        private async Task LoadStaticStorageInfoAsync()
        {
            try
            {
                string instanceDir = PathHelper.GetInstanceDir(InstanceId);
                if (!Directory.Exists(instanceDir)) return;

                // 在后台线程计算目录大小，避免阻塞 UI
                var (totalBytes, worldSizes) = await Task.Run(() =>
                {
                    long total = GetDirectorySize(instanceDir);

                    string[] worldFolders = ["world", "world_nether", "world_the_end"];
                    var sizes = new Dictionary<string, long>();

                    foreach (var worldName in worldFolders)
                    {
                        string worldPath = Path.Combine(instanceDir, worldName);
                        if (Directory.Exists(worldPath))
                        {
                            sizes[worldName] = GetDirectorySize(worldPath) / (1024 * 1024);
                        }
                    }

                    return (total, sizes);
                });

                // 回到 UI 线程更新属性
                RunOnUiThread(() =>
                {
                    TotalStorageMb = totalBytes / (1024 * 1024);
                    TotalStorage = FormatBytes(totalBytes);

                    // 批量更新世界存储信息
                    long maxSize = worldSizes.Values.DefaultIfEmpty(1).Max();
                    var newWorldStorage = worldSizes.Select(kvp => new WorldStorageInfo
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
                    }).ToList();

                    WorldStorageInfo = new ObservableCollection<WorldStorageInfo>(newWorldStorage);
                    StatusMessage = "存储空间统计已刷新";
                });
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
                // 批量替换，避免逐项添加触发多次 UI 更新
                AdminPlayers = new ObservableCollection<PlayerDisplayItem>(
                    ops.Select(op => new PlayerDisplayItem(op.Name, op.Uuid)
                    {
                        IsOp = true,
                        SecondaryText = $"等级 {op.Level}" + (string.IsNullOrWhiteSpace(op.Uuid) ? string.Empty : $"  UUID {op.Uuid}")
                    }));
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
        /// 加载实例的插件/模组列表。
        /// </summary>
        private void LoadPluginMods()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                List<PluginModDisplayItem> items;
                if (IsModServer)
                {
                    var mods = ModManager.GetMods(InstanceId);
                    items = [.. mods.Select(m => new PluginModDisplayItem(m))];
                }
                else
                {
                    var plugins = PluginManager.GetPlugins(InstanceId);
                    items = [.. plugins.Select(p => new PluginModDisplayItem(p))];
                }

                RunOnUiThread(() =>
                {
                    PluginMods = new ObservableCollection<PluginModDisplayItem>(items);
                    OnPropertyChanged(nameof(PluginModTabHeader));
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => StatusMessage = $"加载插件/模组失败: {ex.Message}");
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
                    // 批量替换，避免逐项添加触发多次 UI 更新
                    ServerProperties = new ObservableCollection<ServerProperty>(
                        props.Select(kvp => new ServerProperty(kvp.Key, kvp.Value)));
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
                    _notificationService.ShowDanger("服务器", $"启动失败: {errorMessage}");
                    IsRunning = false;
                    return;
                }

                // 短暂等待检查是否快速退出
                await Task.Delay(500);

                if (!process.IsRunning)
                {
                    StatusMessage = "服务器启动失败，进程已退出";
                    _notificationService.ShowDanger("服务器", "服务器启动失败，进程已退出");
                    try { process.Dispose(); } catch { }
                    IsRunning = false;
                    return;
                }

                // 注册到全局管理器（这会触发 InstanceStatusChanged 事件）
                ServerProcessManager.Register(InstanceId, process);
                _notificationService.ShowSuccess("服务器", $"{InstanceName} 已启动");
            }
            catch (Exception ex)
            {
                IsRunning = false;
                StatusMessage = $"启动失败: {ex.Message}";
                _notificationService.ShowDanger("服务器", $"启动失败: {ex.Message}");
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
                await ServerProcessManager.StopAndRemoveAsync(InstanceId);
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
                        _notificationService.ShowSuccess("服务器", $"{InstanceName} 已停止");
                        return;
                    }
                }

                StatusMessage = "服务器停止超时，可以使用强制终止";
                _notificationService.ShowInfo("服务器", "服务器停止超时，可以使用强制终止");
            }
            catch (Exception ex)
            {
                StatusMessage = $"停止失败: {ex.Message}";
                _notificationService.ShowDanger("服务器", $"停止失败: {ex.Message}");
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
                _notificationService.ShowInfo("服务器", $"{InstanceName} 已被强制终止");
            }
            catch (Exception ex)
            {
                StatusMessage = $"终止失败: {ex.Message}";
                _notificationService.ShowDanger("服务器", $"终止失败: {ex.Message}");
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
            StopServerStatusPolling();
            StopUptimeCounter();
            UnsubscribeFromProcessOutput();
            _consoleThrottler?.Dispose();
            _backupCts?.Dispose();
            _playerRefreshLock?.Dispose();
            ServerProcessManager.InstanceStatusChanged -= OnInstanceStatusChanged;
            OnlinePlayers.CollectionChanged -= OnOnlinePlayersCollectionChanged;
            AdminPlayers.CollectionChanged -= OnAdminPlayersCollectionChanged;
        }

        /// <summary>
        /// 取消订阅服务器进程的输出和错误事件。
        /// </summary>
        private void UnsubscribeFromProcessOutput()
        {
            var process = ServerProcessManager.GetProcess(InstanceId);
            if (process == null) return;

            process.OutputReceived -= OnProcessOutputReceived;
            process.ErrorReceived -= OnProcessErrorReceived;
        }

        private void OnPluginModsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(PluginModsCount));
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
        private async Task SaveSettings()
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

                // 在后台线程执行文件 I/O 操作
                await Task.Run(() =>
                {
                    InstanceManager.UpdateInstance(InstanceInfo);
                    SaveServerPropertiesInternal();
                    InstanceManager.EnsureRconConfiguration(InstanceId);
                });

                InvalidateServerPropsCache();
                var props = GetCachedServerProps();
                UpdateServerAddressFromDict(props);
                LoadServerPropertiesFromDict(props);
                LoadPlayerManagementData();
                StatusMessage = "设置已保存";
                _notificationService.ShowSuccess("设置", "实例设置已保存");
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
                _notificationService.ShowDanger("设置", $"保存失败: {ex.Message}");
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
        /// 删除指定插件/模组及其数据目录。
        /// </summary>
        [RelayCommand]
        private async Task DeletePluginMod(PluginModDisplayItem? plugin)
        {
            if (plugin == null || string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                // 显示确认对话框
                var dialog = new Wpf.Ui.Controls.ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要删除插件/模组 \"{plugin.Name}\" 吗？\n\n此操作将删除插件/模组文件及其数据目录！",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    DefaultButton = Wpf.Ui.Controls.ContentDialogButton.Close
                };

                var result = await _contentDialogService.ShowAsync(dialog, CancellationToken.None);

                if (result != Wpf.Ui.Controls.ContentDialogResult.Primary) return;

                // 先删除插件/模组文件
                string modsOrPluginsDir;
                if (IsModServer)
                {
                    ModManager.DeleteMod(InstanceId, plugin.FileName);
                    modsOrPluginsDir = "mods";
                }
                else
                {
                    PluginManager.DeletePlugin(InstanceId, plugin.FileName);
                    modsOrPluginsDir = "plugins";
                }

                // 删除插件/模组数据目录（如果存在）
                string pluginDataDir = Path.Combine(PathHelper.GetInstanceDir(InstanceId), modsOrPluginsDir, plugin.Name);
                if (Directory.Exists(pluginDataDir))
                {
                    Directory.Delete(pluginDataDir, true); // 递归删除目录
                }

                // 在UI线程上更新插件/模组列表
                RunOnUiThread(() =>
                {
                    PluginMods.Remove(plugin);
                    StatusMessage = "插件/模组已删除";
                    _notificationService.ShowSuccess("插件/模组", $"{plugin.Name} 已删除");
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    StatusMessage = $"删除失败: {ex.Message}";
                    _notificationService.ShowDanger("插件/模组", $"删除失败: {ex.Message}");
                });
            }
        }

        /// <summary>
        /// 刷新插件/模组列表。
        /// </summary>
        [RelayCommand]
        private void RefreshPluginMods()
        {
            LoadPluginMods();
            _notificationService.ShowSuccess("插件/模组", "插件/模组列表已刷新");
        }

        /// <summary>
        /// 在资源管理器中打开指定插件/模组的数据目录。
        /// </summary>
        [RelayCommand]
        private void OpenPluginModDataFolder(PluginModDisplayItem? plugin)
        {
            if (plugin == null || string.IsNullOrEmpty(plugin.FolderPath)) return;

            try
            {
                OpenFolder(plugin.FolderPath, "插件/模组数据目录不存在");
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开目录失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 在资源管理器中打开插件/模组目录。
        /// </summary>
        [RelayCommand]
        private void OpenPluginModsFolder()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                string pluginDir = IsModServer
                    ? PathHelper.GetModsDir(InstanceId)
                    : PathHelper.GetPluginsDir(InstanceId);
                OpenFolder(pluginDir, IsModServer ? "模组目录不存在" : "插件目录不存在");
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开插件/模组目录失败: {ex.Message}";
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
        /// 切换插件/模组的启用/禁用状态（通过重命名 .jar 文件）。
        /// </summary>
        [RelayCommand]
        private async Task TogglePluginModEnabled(PluginModDisplayItem? plugin)
        {
            if (plugin == null || string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                string pluginsDir = IsModServer
                    ? PathHelper.GetModsDir(InstanceId)
                    : PathHelper.GetPluginsDir(InstanceId);

                // 在后台线程执行文件操作
                await Task.Run(() =>
                {
                    if (plugin.IsEnabled)
                    {
                        string pluginFilePath = Path.Combine(pluginsDir, plugin.FileName);
                        string disabledFilePath = Path.ChangeExtension(pluginFilePath, ".jar.dis");

                        if (File.Exists(pluginFilePath))
                        {
                            if (!File.Exists(disabledFilePath))
                            {
                                File.Move(pluginFilePath, disabledFilePath);
                                RunOnUiThread(() =>
                                {
                                    plugin.IsEnabled = false;
                                    StatusMessage = "插件/模组已禁用";
                                    _notificationService.ShowInfo("插件/模组", $"{plugin.Name} 已禁用");
                                });
                            }
                            else
                            {
                                RunOnUiThread(() => StatusMessage = "禁用文件已存在");
                            }
                        }
                        else
                        {
                            RunOnUiThread(() => StatusMessage = "插件/模组文件不存在");
                        }
                    }
                    else
                    {
                        string pluginFileNameWithoutDis = plugin.FileName.EndsWith(".dis")
                            ? plugin.FileName[..^4]
                            : plugin.FileName;

                        string disabledFilePath = Path.Combine(pluginsDir, plugin.FileName);
                        string targetFileName = Path.GetFileNameWithoutExtension(pluginFileNameWithoutDis) + ".jar";
                        string targetPath = Path.Combine(pluginsDir, targetFileName);

                        if (File.Exists(disabledFilePath))
                        {
                            if (!File.Exists(targetPath))
                            {
                                File.Move(disabledFilePath, targetPath);
                                RunOnUiThread(() =>
                                {
                                    plugin.IsEnabled = true;
                                    StatusMessage = "插件/模组已启用";
                                    _notificationService.ShowSuccess("插件/模组", $"{plugin.Name} 已启用");
                                });
                            }
                            else
                            {
                                RunOnUiThread(() => StatusMessage = "启用文件已存在");
                            }
                        }
                        else
                        {
                            RunOnUiThread(() => StatusMessage = "禁用的插件/模组文件不存在");
                        }
                    }
                });

                LoadPluginMods();
            }
            catch (Exception ex)
            {
                StatusMessage = $"切换插件/模组状态失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取服务器配置（带缓存，避免重复磁盘读取）。
        /// </summary>
        private Dictionary<string, string> GetCachedServerProps()
        {
            if (_cachedServerProps != null && _cachedServerPropsInstanceId == InstanceId)
                return _cachedServerProps;

            _cachedServerProps = ServerPropertiesManager.Read(InstanceId);
            _cachedServerPropsInstanceId = InstanceId;
            return _cachedServerProps;
        }

        /// <summary>
        /// 清除服务器配置缓存。
        /// </summary>
        private void InvalidateServerPropsCache()
        {
            _cachedServerProps = null;
            _cachedServerPropsInstanceId = null;
        }

        /// <summary>
        /// 加载仪表盘相关数据（游戏模式、在线模式、服务器地址等）。
        /// </summary>
        public void LoadDashboardData()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            var props = GetCachedServerProps();

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
        /// 停止服务器状态轮询定时器。
        /// </summary>
        private void StopServerStatusPolling()
        {
            _serverStatusTimer?.Dispose();
            _serverStatusTimer = null;
        }

        /// <summary>
        /// 启动服务器运行时长计数器（每秒更新）。
        /// </summary>
        private void StartUptimeCounter()
        {
            _serverStartTime = ServerProcessManager.GetStartTime(InstanceId) ?? DateTime.Now;
            StopUptimeCounter();
            _uptimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uptimeTimer.Tick += OnUptimeTick;
            _uptimeTimer.Start();
        }

        private void OnUptimeTick(object? sender, EventArgs e)
        {
            var uptime = DateTime.Now - _serverStartTime;
            Uptime = $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
        }

        private void StopUptimeCounter()
        {
            if (_uptimeTimer != null)
            {
                _uptimeTimer.Stop();
                _uptimeTimer.Tick -= OnUptimeTick;
                _uptimeTimer = null;
            }
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
                _notificationService.ShowSuccess("玩家", $"已将 {player.Name} 设为管理员");
            }
            catch (Exception ex)
            {
                StatusMessage = $"设置管理员失败: {ex.Message}";
                _notificationService.ShowDanger("玩家", $"设置管理员失败: {ex.Message}");
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
                _notificationService.ShowSuccess("玩家", $"已移除 {player.Name} 的管理员权限");
            }
            catch (Exception ex)
            {
                StatusMessage = $"取消管理员失败: {ex.Message}";
                _notificationService.ShowDanger("玩家", $"取消管理员失败: {ex.Message}");
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
                _notificationService.ShowSuccess("玩家", $"已踢出玩家 {player.Name}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"踢出玩家失败: {ex.Message}";
                _notificationService.ShowDanger("玩家", $"踢出玩家失败: {ex.Message}");
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
                _notificationService.ShowSuccess("玩家", $"已封禁玩家 {player.Name}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"封禁玩家失败: {ex.Message}";
                _notificationService.ShowDanger("玩家", $"封禁玩家失败: {ex.Message}");
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
                _notificationService.ShowSuccess("玩家", $"已封禁 {player.Name} 的 IP");
            }
            catch (Exception ex)
            {
                StatusMessage = $"封禁 IP 失败: {ex.Message}";
                _notificationService.ShowDanger("玩家", $"封禁 IP 失败: {ex.Message}");
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
                _notificationService.ShowSuccess("玩家", $"已向 {player.Name} 发送消息");
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送消息失败: {ex.Message}";
                _notificationService.ShowDanger("玩家", $"发送消息失败: {ex.Message}");
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
    /// 插件/模组显示项
    /// </summary>
    public partial class PluginModDisplayItem : ObservableObject
    {
        public PluginModDisplayItem(PluginInfo info)
        {
            Name = info.Name;
            Version = info.Version;
            Description = info.Description;
            FileName = info.FileName;
            Authors = string.Join(", ", info.Authors);
            FolderPath = string.IsNullOrEmpty(info.FilePath) ? "" :
                Path.Combine(Path.GetDirectoryName(info.FilePath) ?? "", info.Name);
            IsDisabled = info.IsDisabled;
        }

        public PluginModDisplayItem(ModInfo info)
        {
            Name = info.Name;
            Version = info.Version;
            Description = info.Description;
            FileName = info.FileName;
            Authors = string.Join(", ", info.Authors);
            FolderPath = string.IsNullOrEmpty(info.FilePath) ? "" :
                Path.Combine(Path.GetDirectoryName(info.FilePath) ?? "", info.Name);
            IsDisabled = info.IsDisabled;
        }

        public string Name { get; } = "";
        public string Version { get; } = "";
        public string Description { get; } = "";
        public string FileName { get; } = "";
        public string Authors { get; } = "";
        public string FolderPath { get; } = "";
        public bool IsDisabled { get; }
        public bool IsEnabled { get; set; }
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

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            if (bytesRead > 0) _reportProgress?.Invoke(bytesRead);
            return bytesRead;
        }

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

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            while ((bytesRead = await ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            while ((bytesRead = Read(buffer, 0, buffer.Length)) > 0)
            {
                destination.Write(buffer, 0, bytesRead);
            }
        }
        // 其他 Stream 必须重写的成员
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

