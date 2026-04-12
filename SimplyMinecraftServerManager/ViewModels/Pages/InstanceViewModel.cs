using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Models;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Helpers;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class InstanceViewModel : ObservableObject, INavigationAware, IDisposable
    {
        private readonly System.Text.Json.JsonSerializerOptions options = new() { WriteIndented = true };
        private readonly IContentDialogService _contentDialogService;
        private readonly INavigationService _navigationService;
        private readonly NavigationParameterService _navigationParameterService;
        private PerformanceMonitor? _performanceMonitor;
        private readonly SemaphoreSlim _playerRefreshLock = new(1, 1);

        private readonly Lock _consoleLock = new();


        [ObservableProperty]
        private string _instanceId = "";

        [ObservableProperty]
        private InstanceInfo? _instanceInfo;

        [ObservableProperty]
        private string _instanceName = "加载中...";

        [ObservableProperty]
        private string _serverType = "";

        [ObservableProperty]
        private string _minecraftVersion = "";

        [ObservableProperty]
        private bool _isRunning = false;

        [ObservableProperty]
        private bool _isStarting = false;

        [ObservableProperty]
        private bool _autoScroll = true;

        [ObservableProperty]
        private bool _consoleWrapLines = false;

        [ObservableProperty]
        private string _consoleFontFamily = "Consolas";

        [ObservableProperty]
        private int _consoleFontSize = 12;

        [ObservableProperty]
        private bool _isConsoleFullScreen = false;

        private readonly List<string> _consoleLines = [];

        // 控制台内容改变事件，用于通知 UI 更新 FlowDocument
        public event EventHandler<string>? ConsoleLineAdded;
        public event EventHandler? ConsoleCleared;

        public static int MaxConsoleLines => 1000;

        [ObservableProperty]
        private string _commandInput = "";

        [ObservableProperty]
        private ObservableCollection<PluginDisplayItem> _plugins = [];

        [ObservableProperty]
        private ObservableCollection<ServerProperty> _serverProperties = [];

        [ObservableProperty]
        private string _statusMessage = "";

        // 编辑中的属性
        [ObservableProperty]
        private string _editMinMemory = "1024";

        [ObservableProperty]
        private string _editMaxMemory = "2048";

        [ObservableProperty]
        private bool _useCustomJdk = false;

        [ObservableProperty]
        private bool _autoSelectJdk = true;

        [ObservableProperty]
        private string _editJdkPath = "";

        [ObservableProperty]
        private string _editExtraJvmArgs = "";

        [ObservableProperty]
        private ObservableCollection<InstalledJdk> _installedJdks = [];

        [ObservableProperty]
        private InstalledJdk? _selectedInstalledJdk;

        // 性能监控属性 - 保留原有的性能监控属性
        [ObservableProperty]
        private double _cpuUsage = 0;

        [ObservableProperty]
        private double _memoryUsage = 0;

        [ObservableProperty]
        private long _totalStorageMb = 0;

        [ObservableProperty]
        private string _totalStorage = "0 MB";

        [ObservableProperty]
        private ObservableCollection<WorldStorageInfo> _worldStorageInfo = [];

        // 仪表盘新增属性
        [ObservableProperty]
        private string _gameMode = "未知";

        [ObservableProperty]
        private bool _isOnlineMode = false;

        [ObservableProperty]
        private int _simulationDistance = 0;

        [ObservableProperty]
        private int _viewDistance = 0;

        [ObservableProperty]
        private string _serverAddress = "localhost:25565";

        [ObservableProperty]
        private int _onlinePlayersCount = 0;

        [ObservableProperty]
        private int _maxPlayersCount = 0;

        [ObservableProperty]
        private ObservableCollection<PlayerDisplayItem> _onlinePlayers = [];

        [ObservableProperty]
        private ObservableCollection<PlayerDisplayItem> _adminPlayers = [];

        [ObservableProperty]
        private int _playerDataCount = 0;

        [ObservableProperty]
        private string _onlinePlayersHint = "启动服务器以查看";

        [ObservableProperty]
        private bool _isRefreshingPlayers = false;

        public bool ShowOnlinePlayersHint => OnlinePlayers.Count == 0;
        public bool ShowAdminPlayersHint => AdminPlayers.Count == 0;

        [ObservableProperty]
        private string _uptime = "00:00:00";

        [ObservableProperty]
        private long _networkSentBytes = 0;

        [ObservableProperty]
        private long _networkReceivedBytes = 0;

        public int MaxMemoryMb => InstanceInfo?.MaxMemoryMb ?? 2048;

        private Timer? _serverStatusTimer;
        private Timer? _uptimeTimer;
        private DateTime _serverStartTime = DateTime.MinValue;
        private PerformanceMonitor? _dashboardPerformanceMonitor;

        public InstanceViewModel(
            IContentDialogService contentDialogService,
            INavigationService navigationService,
            NavigationParameterService navigationParameterService)
        {
            _contentDialogService = contentDialogService;
            _navigationService = navigationService;
            _navigationParameterService = navigationParameterService;
            LoadConsolePreferences();
            OnlinePlayers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowOnlinePlayersHint));
            AdminPlayers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowAdminPlayersHint));

            // 订阅全局状态变化事件
            ServerProcessManager.InstanceStatusChanged += OnInstanceStatusChanged;
        }

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
                    StopDashboardMonitoring();
                    ResetOnlinePlayersState();
                }
                else
                {
                    StatusMessage = "服务器运行中";
                    QueueRunningStateInitialization();
                }
            });
        }

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

        private void OnProcessOutputReceived(object? sender, string line)
        {
            lock (_pendingConsoleLock)
            {
                _pendingConsoleLines.Add(line);
            }
            EnsureConsoleThrottler();
        }

        private void OnProcessErrorReceived(object? sender, string line)
        {
            lock (_pendingConsoleLock)
            {
                _pendingConsoleLines.Add("[ERR] " + line);
            }
            EnsureConsoleThrottler();
        }

        private void EnsureConsoleThrottler()
        {
            _consoleThrottler ??= new ThrottledDispatcher(DispatcherPriority.Background, 50);
            _consoleThrottler.Invoke(FlushPendingConsoleLines);
        }

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

        private void AppendConsoleLineInternal(string line)
        {
            lock (_consoleLock)
            {
                _consoleLines.Add(line);
                if (_consoleLines.Count > MaxConsoleLines)
                {
                    _consoleLines.RemoveAt(0);
                }
            }
            ConsoleLineAdded?.Invoke(this, line);
        }

private void AppendConsoleLine(string line)
        {
            AppendConsoleLineInternal(line);
        }

        private static void RunOnUiThread(Action action)
        {
            DispatcherHelper.InvokeIfNeeded(action);
        }

        [RelayCommand]
        private void ClearConsole()
        {
            lock (_consoleLock)
            {
                _consoleLines.Clear();
            }
            ConsoleCleared?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void CopyConsole()
        {
            lock (_consoleLock)
            {
                var text = string.Join(Environment.NewLine, _consoleLines);
                System.Windows.Clipboard.SetText(text);
                StatusMessage = "控制台内容已复制到剪贴板";
            }
        }

        /// <summary>
        /// 获取所有控制台文本（用于初始化 FlowDocument）
        /// </summary>
        public string GetConsoleText()
        {
            lock (_consoleLock)
            {
                return string.Join(Environment.NewLine, _consoleLines);
            }
        }

        public IReadOnlyList<string> GetConsoleLines()
        {
            lock (_consoleLock)
            {
                return [.. _consoleLines];
            }
        }

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

        public Task OnNavigatedFromAsync()
        {
            IsConsoleFullScreen = false;
            StopPerformanceMonitoring();
            StopDashboardMonitoring();
            return Task.CompletedTask;
        }

        public void LoadInstance(string instanceId)
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

            // 加载已安装的JDK列表
            LoadInstalledJdks();
            InitializeJdkSelectionState(info.JdkPath);

            // 从 ServerProcessManager 恢复运行状态
            IsRunning = ServerProcessManager.IsRunning(instanceId);
            StatusMessage = IsRunning ? "服务器运行中" : $"{ServerType} - Minecraft {MinecraftVersion}";

            // 如果正在运行，订阅控制台输出和性能监控
            if (IsRunning)
            {
                QueueRunningStateInitialization();
            }
            else
            {
                // 未运行时加载静态存储信息
                LoadStaticStorageInfo();
                ResetOnlinePlayersState();
            }

            LoadPlugins();
            LoadServerProperties();
            LoadPlayerManagementData();
            
            // 加载仪表盘数据
            LoadDashboardData();
        }

        private void LoadInstalledJdks()
        {
            try
            {
                var installedJdks = JdkManager.GetInstalledJdks();
                InstalledJdks.Clear();
                foreach (var jdk in installedJdks)
                {
                    InstalledJdks.Add(jdk);
                }
            }
            catch (Exception ex)
            {
                // 静默失败，不影响主功能
                System.Diagnostics.Debug.WriteLine($"加载已安装JDK失败: {ex.Message}");
            }
        }

        private void LoadConsolePreferences()
        {
            var config = ConfigManager.Current;
            ConsoleWrapLines = config.ConsoleWrapLines;
            ConsoleFontFamily = string.IsNullOrWhiteSpace(config.ConsoleFontFamily) ? "Consolas" : config.ConsoleFontFamily;
            ConsoleFontSize = Math.Clamp(config.ConsoleFontSize, 10, 32);
        }

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

        private static long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Exists)
                            size += fi.Length;
                    }
                    catch { }
                }
            }
            catch { }
            return size;
        }

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

        private void LoadAdminPlayers()
        {
            AdminPlayers.Clear();

            foreach (var op in ReadOps())
            {
                AdminPlayers.Add(new PlayerDisplayItem(op.Name, op.Uuid)
                {
                    IsOp = true,
                    SecondaryText = $"等级 {op.Level}" + (string.IsNullOrWhiteSpace(op.Uuid) ? string.Empty : $"  UUID {op.Uuid}")
                });
            }
        }

        private List<OpEntry> ReadOps()
        {
            try
            {
                string opsFilePath = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "ops.json");
                if (!File.Exists(opsFilePath))
                {
                    return [];
                }

                var ops = System.Text.Json.JsonSerializer.Deserialize<List<OpEntry>>(File.ReadAllText(opsFilePath));
                return ops?
                    .Where(op => !string.IsNullOrWhiteSpace(op.Name))
                    .OrderBy(op => op.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [];
            }
            catch
            {
                return [];
            }
        }

        private void WriteOps(IEnumerable<OpEntry> ops)
        {
            string opsFilePath = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "ops.json");
            Directory.CreateDirectory(Path.GetDirectoryName(opsFilePath)!);
            File.WriteAllText(
                opsFilePath,
                System.Text.Json.JsonSerializer.Serialize(ops.OrderBy(op => op.Name, StringComparer.OrdinalIgnoreCase), options));
        }

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

        private void QueueRunningStateInitialization()
        {
            _ = InitializeRunningStateAsync();
        }

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

        private void OnPerformanceDataUpdated(object? sender, PerformanceData data)
        {
            RunOnUiThread(() =>
            {
                CpuUsage = data.CpuUsage;
                MemoryUsage = data.MemoryUsageMb;
                TotalStorageMb = data.TotalStorageMb;
                TotalStorage = FormatBytes(data.TotalStorageMb * 1024 * 1024);

                // 更新世界存储信息
                WorldStorageInfo.Clear();
                long maxSize = data.WorldStorageMb.Values.DefaultIfEmpty(1).Max();

                foreach (var kvp in data.WorldStorageMb)
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
            });
        }

        private void StopPerformanceMonitoring()
        {
            _performanceMonitor?.Dispose();
            _performanceMonitor = null;

            // 只重置运行时性能数据（CPU 和内存）
            // 存储信息保留，因为世界文件仍然存在
            CpuUsage = 0;
            MemoryUsage = 0;
        }

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

        private void LoadPlugins()
        {
            Plugins.Clear();
            if (string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                var plugins = PluginManager.GetPlugins(InstanceId);
                foreach (var p in plugins)
                {
                    Plugins.Add(new PluginDisplayItem(p));
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载插件失败: {ex.Message}";
            }
        }

        private void LoadServerProperties()
        {
            ServerProperties.Clear();
            if (string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                var props = ServerPropertiesManager.Read(InstanceId);
                foreach (var kvp in props)
                {
                    ServerProperties.Add(new ServerProperty(kvp.Key, kvp.Value));
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载配置失败: {ex.Message}";
            }
        }

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
                var (process, success, errorMessage) = await Task.Run(() =>
                {
                    try
                    {
                        var proc = new ServerProcess(InstanceId);

                        // 设置控制台输出事件
                        proc.OutputReceived += OnProcessOutputReceived;
                        proc.ErrorReceived += OnProcessErrorReceived;

                        // 启动进程（这个操作可能耗时）
                        proc.StartAsync().GetAwaiter().GetResult();

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
                StopDashboardMonitoring(); // 停止仪表盘监控
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
                        StopDashboardMonitoring(); // 停止仪表盘监控
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

        [RelayCommand]
        private void TerminateServer()
        {
            if (!IsRunning) return;

            // 检查进程是否真的在运行
            if (!ServerProcessManager.IsRunning(InstanceId))
            {
                IsRunning = false;
                StopPerformanceMonitoring();
                StopDashboardMonitoring(); // 停止仪表盘监控
                StatusMessage = "服务器未在运行";
                return;
            }

            try
            {
                ServerProcessManager.KillAndRemove(InstanceId);
                IsRunning = false;
                StopPerformanceMonitoring();
                StopDashboardMonitoring(); // 停止仪表盘监控
                StatusMessage = "服务器已被强制终止";
            }
            catch (Exception ex)
            {
                StatusMessage = $"终止失败: {ex.Message}";
            }
        }

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

        public void Dispose()
        {
            StopDashboardMonitoring(); // 确保停止仪表盘监控
            StopPerformanceMonitoring();
            ServerProcessManager.InstanceStatusChanged -= OnInstanceStatusChanged;
            GC.SuppressFinalize(this);
        }

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

                InstanceManager.UpdateInstance(InstanceInfo);
                SaveServerPropertiesInternal();
                InstanceManager.EnsureRconConfiguration(InstanceId);
                UpdateServerAddress();
                LoadServerPropertiesForDashboard();
                LoadPlayerManagementData();
                StatusMessage = "设置已保存";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void EnterConsoleFullScreen() => IsConsoleFullScreen = true;

        [RelayCommand]
        private void ExitConsoleFullScreen() => IsConsoleFullScreen = false;

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

        [RelayCommand]
        private void RefreshPlugins()
        {
            LoadPlugins();
        }

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

        // 仪表盘相关方法
        public void LoadDashboardData()
        {
            if (string.IsNullOrEmpty(InstanceId)) return;

            // 读取 server.properties
            LoadServerPropertiesForDashboard();
            
            // 设置服务器地址
            UpdateServerAddress();
            
            // 如果服务器正在运行，启动状态轮询
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

        private void LoadServerPropertiesForDashboard()
        {
            try
            {
                var props = ServerPropertiesManager.Read(InstanceId);
                
                // 读取游戏模式
                var gameModeValue = props.GetValueOrDefault("gamemode", "survival");
                GameMode = gameModeValue switch
                {
                    "survival" => "生存模式",
                    "creative" => "创造模式",
                    "adventure" => "冒险模式",
                    "spectator" => "旁观模式",
                    _ => gameModeValue
                };

                // 读取在线模式
                IsOnlineMode = props.GetValueOrDefault("online-mode", "true").Equals("true", StringComparison.CurrentCultureIgnoreCase);

                // 读取模拟距离
                SimulationDistance = int.TryParse(props.GetValueOrDefault("simulation-distance", "0"), out var simDist) ? simDist : 0;

                // 读取视距
                ViewDistance = int.TryParse(props.GetValueOrDefault("view-distance", "0"), out var viewDist) ? viewDist : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载服务器属性失败: {ex.Message}");
            }
        }

        private void UpdateServerAddress()
        {
            try
            {
                var props = ServerPropertiesManager.Read(InstanceId);
                var ip = props.GetValueOrDefault("server-ip", "");
                var port = props.GetValueOrDefault("server-port", "25565");
                
                if (string.IsNullOrWhiteSpace(ip))
                {
                    ServerAddress = $"localhost:{port}";
                }
                else
                {
                    ServerAddress = $"{ip}:{port}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新服务器地址失败: {ex.Message}");
                ServerAddress = "localhost:25565";
            }
        }

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

        private static OnlinePlayersState ParseOnlinePlayersResponse(string response)
        {
            string normalized = (response ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            var numbers = Regex.Matches(normalized, @"\d+")
                .Select(match => int.TryParse(match.Value, out int value) ? value : 0)
                .ToList();

            int onlineCount = numbers.Count > 0 ? numbers[0] : 0;
            int maxPlayers = numbers.Count > 1 ? numbers[1] : 0;

            int separatorIndex = Math.Max(normalized.LastIndexOf(':'), normalized.LastIndexOf('：'));
            var playerNames = new List<string>();
            if (separatorIndex >= 0 && separatorIndex < normalized.Length - 1)
            {
                playerNames = normalized[(separatorIndex + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return new OnlinePlayersState(onlineCount, maxPlayers, playerNames);
        }

        private bool IsPlayerOp(string playerName)
        {
            return ReadOps().Any(op => string.Equals(op.Name, playerName, StringComparison.OrdinalIgnoreCase));
        }

        private void StartServerStatusPolling()
        {
            _serverStatusTimer?.Dispose();
            _serverStatusTimer = new Timer(_ =>
            {
                _ = RefreshOnlinePlayersAsync();
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        private void StartUptimeCounter()
        {
            _serverStartTime = ServerProcessManager.GetStartTime(InstanceId) ?? DateTime.Now;
            _uptimeTimer?.Dispose();
            _uptimeTimer = new Timer((state) =>
            {
                var uptime = DateTime.Now - _serverStartTime;
                Uptime = $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1)); // 每秒更新一次
        }

        public void StartDashboardPerformanceMonitoring()
        {
            try
            {
                _dashboardPerformanceMonitor?.Dispose();
                _dashboardPerformanceMonitor = new PerformanceMonitor(InstanceId);
                _dashboardPerformanceMonitor.DataUpdated += OnDashboardPerformanceDataUpdated;
                _dashboardPerformanceMonitor.Start();
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    StatusMessage = $"仪表盘性能监控启动失败: {ex.Message}";
                });
            }
        }

        private void OnDashboardPerformanceDataUpdated(object? sender, PerformanceData data)
        {
            RunOnUiThread(() =>
            {
                CpuUsage = data.CpuUsage;
                MemoryUsage = data.MemoryUsageMb;
                TotalStorageMb = data.TotalStorageMb;
                TotalStorage = FormatBytes(data.TotalStorageMb * 1024 * 1024);
            });
        }

        public void StopDashboardMonitoring()
        {
            _serverStatusTimer?.Dispose();
            _uptimeTimer?.Dispose();
            _dashboardPerformanceMonitor?.Dispose();
        }

        [RelayCommand]
        private void CopyServerAddress()
        {
            Clipboard.SetText(ServerAddress);
            StatusMessage = "服务器地址已复制到剪贴板";
        }

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

        [RelayCommand]
        private async Task KickPlayer(PlayerDisplayItem? player)
        {
            if (player == null)
            {
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

        [RelayCommand]
        private async Task BanPlayer(PlayerDisplayItem? player)
        {
            if (player == null)
            {
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

        [RelayCommand]
        private async Task BanPlayerIp(PlayerDisplayItem? player)
        {
            if (player == null)
            {
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

        [RelayCommand]
        private async Task SendMessageToPlayer(PlayerDisplayItem? player)
        {
            if (player == null)
            {
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
                    new TextBlock
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
                string singleLineMessage = Regex.Replace(message, @"\s+", " ").Trim();
                await ExecutePlayerRconCommandAsync($"msg {player.Name} {singleLineMessage}");
                StatusMessage = $"已向 {player.Name} 发送消息";
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送消息失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RefreshPlayers()
        {
            LoadPlayerManagementData();
            StatusMessage = "玩家信息已刷新";
        }

        private async Task ExecutePlayerRconCommandAsync(string command)
        {
            var process = ServerProcessManager.GetProcess(InstanceId);
            if (process == null || !process.IsRunning)
            {
                throw new InvalidOperationException("服务器未运行，无法执行该操作。");
            }

            await process.ExecuteRconCommandAsync(command);
        }

        [RelayCommand]
        private void RefreshDashboard()
        {
            LoadDashboardData();
            StatusMessage = "仪表盘数据已刷新";
        }
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

    public class OpEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = "";

        [JsonPropertyName("level")]
        public int Level { get; set; } = 4;

        [JsonPropertyName("bypassesPlayerLimit")]
        public bool BypassesPlayerLimit { get; set; } = false;
    }

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
}
