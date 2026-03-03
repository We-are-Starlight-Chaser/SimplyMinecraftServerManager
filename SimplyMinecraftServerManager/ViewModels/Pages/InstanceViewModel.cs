using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class InstanceViewModel : ObservableObject, INavigationAware, IDisposable
    {
        private readonly INavigationService _navigationService;
        private readonly NavigationParameterService _navigationParameterService;
        private PerformanceMonitor? _performanceMonitor;

        private readonly object _consoleLock = new();

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

        public int MaxConsoleLines => 1000;

        [ObservableProperty]
        private string _commandInput = "";

        [ObservableProperty]
        private ObservableCollection<PluginDisplayItem> _plugins = [];

        [ObservableProperty]
        private ObservableCollection<KeyValuePair<string, string>> _serverProperties = [];

        [ObservableProperty]
        private string _statusMessage = "";

        // 编辑中的属性
        [ObservableProperty]
        private string _editMinMemory = "1024";

        [ObservableProperty]
        private string _editMaxMemory = "2048";

        [ObservableProperty]
        private string _editJdkPath = "";

        // 性能监控属性
        [ObservableProperty]
        private double _cpuUsage = 0;

        [ObservableProperty]
        private double _memoryUsage = 0;

        [ObservableProperty]
        private long _totalStorageMb = 0;

        [ObservableProperty]
        private string _totalStorage = "0 MB";

        [ObservableProperty]
        private ObservableCollection<WorldStorageInfo> _worldStorageInfo = new();

        public int MaxMemoryMb => InstanceInfo?.MaxMemoryMb ?? 2048;

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
                }
                else
                {
                    StatusMessage = "服务器运行中";
                    // 重新订阅控制台输出
                    SubscribeToProcessOutput();
                    // 启动性能监控
                    StartPerformanceMonitoring();
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

            // 从 ServerProcessManager 恢复运行状态
            IsRunning = ServerProcessManager.IsRunning(instanceId);
            StatusMessage = IsRunning ? "服务器运行中" : $"{info.ServerType} - Minecraft {info.MinecraftVersion}";

            // 如果正在运行，订阅控制台输出和性能监控
            if (IsRunning)
            {
                SubscribeToProcessOutput();
                StartPerformanceMonitoring();
            }
            else
            {
                // 未运行时加载静态存储信息
                LoadStaticStorageInfo();
            }

            LoadPlugins();
            LoadServerProperties();
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
                string[] worldFolders = new[] { "world", "world_nether", "world_the_end" };
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

        private long GetDirectorySize(string path)
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
                    ServerProperties.Add(new KeyValuePair<string, string>(kvp.Key, kvp.Value));
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
            ServerProcessManager.InstanceStatusChanged -= OnInstanceStatusChanged;
            StopPerformanceMonitoring();
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

                InstanceInfo.JdkPath = EditJdkPath;

                InstanceManager.UpdateInstance(InstanceInfo);
                StatusMessage = "设置已保存";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void DeletePlugin(PluginDisplayItem? plugin)
        {
            if (plugin == null || string.IsNullOrEmpty(InstanceId)) return;

            try
            {
                PluginManager.DeletePlugin(InstanceId, plugin.FileName);
                Plugins.Remove(plugin);
                StatusMessage = "插件已删除";
            }
            catch (Exception ex)
            {
                StatusMessage = $"删除失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RefreshPlugins()
        {
            LoadPlugins();
        }
    }

    public class PluginDisplayItem
    {
        public string Name { get; }
        public string Version { get; }
        public string Description { get; }
        public string FileName { get; }
        public string Authors { get; }

        public PluginDisplayItem(PluginInfo info)
        {
            Name = info.Name;
            Version = info.Version;
            Description = info.Description;
            FileName = info.FileName;
            Authors = string.Join(", ", info.Authors);
        }
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
