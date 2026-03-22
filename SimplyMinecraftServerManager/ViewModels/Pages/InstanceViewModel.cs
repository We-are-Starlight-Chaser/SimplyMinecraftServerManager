using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Models;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class InstanceViewModel : ObservableObject, INavigationAware, IDisposable
    {
        private readonly System.Text.Json.JsonSerializerOptions options = new() { WriteIndented = true };
        private readonly INavigationService _navigationService;
        private readonly NavigationParameterService _navigationParameterService;
        private PerformanceMonitor? _performanceMonitor;

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
        private bool _autoScroll = true;

        private readonly StringBuilder _consoleBuilder = new();
        private int _lineCount = 0;

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

        public InstanceViewModel(INavigationService navigationService, NavigationParameterService navigationParameterService)
        {
            _navigationService = navigationService;
            _navigationParameterService = navigationParameterService;

            // 订阅全局状态变化事件
            ServerProcessManager.InstanceStatusChanged += OnInstanceStatusChanged;
        }

        private void OnInstanceStatusChanged(object? sender, (string InstanceId, bool IsRunning) e)
        {
            if (e.InstanceId != InstanceId) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                IsRunning = e.IsRunning;
                if (!e.IsRunning)
                {
                    StatusMessage = "服务器已停止";
                    StopPerformanceMonitoring();
                    StopDashboardMonitoring();
                }
                else
                {
                    StatusMessage = "服务器运行中";
                    // 重新订阅控制台输出
                    SubscribeToProcessOutput();
                    // 启动性能监控
                    StartPerformanceMonitoring();
                    StartDashboardPerformanceMonitoring(); // 启动仪表盘性能监控
                    LoadDashboardData(); // 加载仪表盘数据
                    StartServerStatusPolling(); // 启动服务器状态轮询
                    StartUptimeCounter(); // 启动运行时间计数
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

        private void OnProcessOutputReceived(object? sender, string line)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AppendConsoleLine(line);
            });
        }

        private void OnProcessErrorReceived(object? sender, string line)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AppendConsoleLine("[ERR] " + line);
            });
        }

        private void AppendConsoleLine(string line)
        {
            lock (_consoleLock)
            {
                _consoleBuilder.AppendLine(line);
                _lineCount++;

                // 限制行数
                if (_lineCount > MaxConsoleLines)
                {
                    // 找到第一个换行符并移除之前的所有内容
                    var content = _consoleBuilder.ToString();
                    var firstNewLine = content.IndexOf('\n');
                    if (firstNewLine >= 0 && firstNewLine + 1 < content.Length)
                    {
                        _consoleBuilder.Remove(0, firstNewLine + 1);
                        _lineCount--;
                    }
                }
            }

            // 触发事件通知 UI
            ConsoleLineAdded?.Invoke(this, line);
        }

        [RelayCommand]
        private void ClearConsole()
        {
            lock (_consoleLock)
            {
                _consoleBuilder.Clear();
                _lineCount = 0;
            }
            ConsoleCleared?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void CopyConsole()
        {
            lock (_consoleLock)
            {
                var text = _consoleBuilder.ToString();
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
                return _consoleBuilder.ToString();
            }
        }

        public async Task OnNavigatedToAsync()
        {
            // 从导航参数服务获取实例 ID
            var instanceId = _navigationParameterService.GetAndClearInstanceId();
            if (!string.IsNullOrEmpty(instanceId))
            {
                LoadInstance(instanceId);
            }
            await Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

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
            ServerType = info.ServerType;
            MinecraftVersion = info.MinecraftVersion;
            EditMinMemory = info.MinMemoryMb.ToString();
            EditMaxMemory = info.MaxMemoryMb.ToString();
            EditJdkPath = info.JdkPath;
            EditExtraJvmArgs = info.ExtraJvmArgs;
            UseCustomJdk = !string.IsNullOrEmpty(info.JdkPath);

            // 加载已安装的JDK列表
            LoadInstalledJdks();

            // 初始化JDK选择逻辑
            if (!UseCustomJdk)
            {
                // 如果当前有JdkPath，尝试找到对应的已安装JDK
                if (!string.IsNullOrEmpty(info.JdkPath))
                {
                    var matchingJdk = InstalledJdks.FirstOrDefault(j => 
                        j.JavaExecutable.Equals(info.JdkPath, StringComparison.OrdinalIgnoreCase));
                    if (matchingJdk != null)
                    {
                        SelectedInstalledJdk = matchingJdk;
                        AutoSelectJdk = false;
                    }
                    else
                    {
                        AutoSelectJdk = true;
                    }
                }
                else
                {
                    AutoSelectJdk = true;
                }
            }

            // 从 ServerProcessManager 恢复运行状态
            IsRunning = ServerProcessManager.IsRunning(instanceId);
            StatusMessage = IsRunning ? "服务器运行中" : $"{info.ServerType} - Minecraft {info.MinecraftVersion}";

            // 如果正在运行，订阅控制台输出和性能监控
            if (IsRunning)
            {
                SubscribeToProcessOutput();
                StartPerformanceMonitoring();
                StartDashboardPerformanceMonitoring(); // 启动仪表盘性能监控
                LoadDashboardData(); // 加载仪表盘数据
                StartServerStatusPolling(); // 启动服务器状态轮询
                StartUptimeCounter(); // 启动运行时间计数
            }
            else
            {
                // 未运行时加载静态存储信息
                LoadStaticStorageInfo();
                // 服务器未运行时，清空在线玩家信息
                OnlinePlayersCount = 0;
                MaxPlayersCount = 0;
                OnlinePlayers.Clear();
            }

            LoadPlugins();
            LoadServerProperties();
            
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
                StatusMessage = $"性能监控启动失败: {ex.Message}";
            }
        }

        private void OnPerformanceDataUpdated(object? sender, PerformanceData data)
        {
            Application.Current.Dispatcher.Invoke(() =>
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
            if (IsRunning || InstanceInfo == null) return;

            try
            {
                // 检查是否已经在运行
                if (ServerProcessManager.IsRunning(InstanceId))
                {
                    IsRunning = true;
                    StatusMessage = "服务器已在运行";
                    SubscribeToProcessOutput();
                    StartPerformanceMonitoring();
                    StartDashboardPerformanceMonitoring(); // 启动仪表盘性能监控
                    LoadDashboardData(); // 加载仪表盘数据
                    StartServerStatusPolling(); // 启动服务器状态轮询
                    StartUptimeCounter(); // 启动运行时间计数
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
                        proc.Start();

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

                IsRunning = true;
                StatusMessage = "服务器已启动";

                // 启动性能监控
                StartPerformanceMonitoring();
                StartDashboardPerformanceMonitoring(); // 启动仪表盘性能监控
                LoadDashboardData(); // 加载仪表盘数据
                StartServerStatusPolling(); // 启动服务器状态轮询
                StartUptimeCounter(); // 启动运行时间计数
            }
            catch (Exception ex)
            {
                IsRunning = false;
                StatusMessage = $"启动失败: {ex.Message}";
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
                if (int.TryParse(EditMinMemory, out int minMem))
                    InstanceInfo.MinMemoryMb = minMem;

                if (int.TryParse(EditMaxMemory, out int maxMem))
                    InstanceInfo.MaxMemoryMb = maxMem;

                // JDK路径处理逻辑
                if (UseCustomJdk)
                {
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
                StatusMessage = "设置已保存";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
            }
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

                var result = await dialog.ShowAsync();

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
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Plugins.Remove(plugin);
                    StatusMessage = "插件已删除";
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
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
                if (Directory.Exists(plugin.FolderPath))
                {
                    System.Diagnostics.Process.Start("explorer", plugin.FolderPath);
                }
                else
                {
                    StatusMessage = "插件数据目录不存在";
                }
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
                if (Directory.Exists(pluginsDir))
                {
                    System.Diagnostics.Process.Start("explorer", pluginsDir);
                }
                else
                {
                    StatusMessage = "插件目录不存在";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开插件目录失败: {ex.Message}";
            }
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
                // 服务器未运行时，清空在线玩家信息
                OnlinePlayersCount = 0;
                MaxPlayersCount = 0;
                OnlinePlayers.Clear();
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

        private void LoadServerStatus()
        {
            try
            {
                var addressParts = ServerAddress.Split(':');
                if (addressParts.Length >= 2)
                {
                    var host = addressParts[0];
                    if (int.TryParse(addressParts[1], out var port))
                    {
                        var status = MinecraftServerPing.Ping(host, port, 3000);
                        if (status != null)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                OnlinePlayersCount = status.OnlinePlayers;
                                MaxPlayersCount = status.MaxPlayers;

                                OnlinePlayers.Clear();
                                foreach (var player in status.Players)
                                {
                                    // 检查玩家是否是OP
                                    bool isOp = IsPlayerOp(player.Name);
                                    OnlinePlayers.Add(new PlayerDisplayItem(player.Name, player.Id) { IsOp = isOp });
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取服务器状态失败: {ex.Message}");
            }
        }

        private bool IsPlayerOp(string playerName)
        {
            try
            {
                string opsFilePath = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "ops.json");
                if (File.Exists(opsFilePath))
                {
                    var opsJson = System.Text.Json.JsonSerializer.Deserialize<OpEntry[]>(File.ReadAllText(opsFilePath));
                    if (opsJson != null)
                    {
                        foreach (var op in opsJson)
                        {
                            if (string.Equals(op.Name, playerName, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查OP状态失败: {ex.Message}");
            }
            return false;
        }

        private void StartServerStatusPolling()
        {
            _serverStatusTimer?.Dispose();
            _serverStatusTimer = new Timer(async (state) =>
            {
                await Task.Run(() => LoadServerStatus());
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5)); // 每5秒更新一次
        }

        private void StartUptimeCounter()
        {
            _serverStartTime = DateTime.Now;
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
                StatusMessage = $"仪表盘性能监控启动失败: {ex.Message}";
            }
        }

        private void OnDashboardPerformanceDataUpdated(object? sender, PerformanceData data)
        {
            Application.Current.Dispatcher.Invoke(() =>
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
        private void SetPlayerAsOp(PlayerDisplayItem? player)
        {
            if (player == null || string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                // 检查ops.json文件是否存在
                string opsFilePath = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "ops.json");
                var ops = new List<OpEntry>();
                
                if (File.Exists(opsFilePath))
                {
                    var opsJson = System.Text.Json.JsonSerializer.Deserialize<OpEntry[]>(File.ReadAllText(opsFilePath));
                    if (opsJson != null)
                    {
                        ops = [.. opsJson];
                    }
                }

                // 检查玩家是否已经是OP
                var existingOp = ops.Find(op => string.Equals(op.Name, player.Name, StringComparison.OrdinalIgnoreCase));
                if (existingOp == null)
                {
                    // 添加为OP
                    ops.Add(new OpEntry
                    {
                        Name = player.Name,
                        Uuid = player.Id,
                        Level = 4,
                        BypassesPlayerLimit = false
                    });

                    // 写入文件
                    File.WriteAllText(opsFilePath, System.Text.Json.JsonSerializer.Serialize(ops.ToArray(), options));
                    
                    // 更新UI状态
                    player.IsOp = true;
                    StatusMessage = $"已将 {player.Name} 设置为OP";
                }
                else
                {
                    StatusMessage = $"{player.Name} 已经是OP";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"设置OP失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RemovePlayerOp(PlayerDisplayItem? player)
        {
            if (player == null || string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                // 检查ops.json文件是否存在
                string opsFilePath = Path.Combine(PathHelper.GetInstanceDir(InstanceId), "ops.json");
                
                if (File.Exists(opsFilePath))
                {
                    var ops = System.Text.Json.JsonSerializer.Deserialize<OpEntry[]>(File.ReadAllText(opsFilePath));
                    if (ops != null)
                    {
                        var opsList = new List<OpEntry>(ops);
                        var opToRemove = opsList.Find(op => string.Equals(op.Name, player.Name, StringComparison.OrdinalIgnoreCase));
                        
                        if (opToRemove != null)
                        {
                            opsList.Remove(opToRemove);
                            
                            // 写入文件
                            File.WriteAllText(opsFilePath, System.Text.Json.JsonSerializer.Serialize(opsList.ToArray(), options));
                            
                            // 更新UI状态
                            player.IsOp = false;
                            StatusMessage = $"已移除 {player.Name} 的OP权限";
                        }
                        else
                        {
                            StatusMessage = $"{player.Name} 不是OP";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"移除OP失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void KickPlayer(PlayerDisplayItem? player)
        {
            if (player == null || string.IsNullOrEmpty(InstanceId)) return;

            var process = ServerProcessManager.GetProcess(InstanceId);
            if (process != null && process.IsRunning)
            {
                process.SendCommand($"kick {player.Name}");
                StatusMessage = $"已踢出玩家 {player.Name}";
            }
            else
            {
                StatusMessage = "服务器未运行，无法踢出玩家";
            }
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
        public string Name { get; set; } = name;
        public string Id { get; set; } = id;

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
        public string Name { get; set; } = "";
        public string Uuid { get; set; } = "";
        public int Level { get; set; } = 4;
        public bool BypassesPlayerLimit { get; set; } = false;
    }

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
