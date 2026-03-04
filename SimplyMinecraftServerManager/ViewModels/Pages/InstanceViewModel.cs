using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Services;
using System.Collections.ObjectModel;
using System.Text;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class InstanceViewModel(INavigationService navigationService, NavigationParameterService navigationParameterService) : ObservableObject, INavigationAware
    {
        private readonly INavigationService _navigationService = navigationService;
        private readonly NavigationParameterService _navigationParameterService = navigationParameterService;
        private readonly StringBuilder _consoleBuilder = new();
        private const int MaxConsoleLines = 1000;
        private int _lineCount = 0;

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
        private string _consoleOutput = "";

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

        private ServerProcess? _serverProcess;

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
                return;
            }

            InstanceInfo = info;
            InstanceName = info.Name;
            ServerType = info.ServerType;
            MinecraftVersion = info.MinecraftVersion;
            EditMinMemory = info.MinMemoryMb.ToString();
            EditMaxMemory = info.MaxMemoryMb.ToString();
            EditJdkPath = info.JdkPath;
            StatusMessage = $"{info.ServerType} - Minecraft {info.MinecraftVersion}";

            LoadPlugins();
            LoadServerProperties();
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
        private void StartServer()
        {
            if (IsRunning || InstanceInfo == null) return;

            try
            {
                _serverProcess?.Dispose();
                _serverProcess = new ServerProcess(InstanceId);

                _consoleBuilder.Clear();
                _lineCount = 0;
                ConsoleOutput = "";

                _serverProcess.OutputReceived += (_, line) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppendConsoleLine(line);
                    });
                };

                _serverProcess.ErrorReceived += (_, line) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppendConsoleLine("[ERR] " + line);
                    });
                };

                _serverProcess.Exited += (_, code) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsRunning = false;
                        StatusMessage = $"服务器已退出，代码: {code}";
                    });
                };

                _serverProcess.Start();
                IsRunning = true;
                StatusMessage = "服务器已启动";
            }
            catch (Exception ex)
            {
                StatusMessage = $"启动失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void StopServer()
        {
            if (!IsRunning || _serverProcess == null) return;

            try
            {
                _serverProcess.Stop();
                StatusMessage = "正在停止服务器...";
            }
            catch (Exception ex)
            {
                StatusMessage = $"停止失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void TerminateServer()
        {
            if (!IsRunning || _serverProcess == null) return;

            try
            {
                _serverProcess.Kill();
                IsRunning = false;
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
            if (string.IsNullOrWhiteSpace(CommandInput) || _serverProcess == null || !IsRunning) return;

            try
            {
                _serverProcess.SendCommand(CommandInput);
                CommandInput = "";
            }
            catch (Exception ex)
            {
                StatusMessage = $"发送命令失败: {ex.Message}";
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

        [RelayCommand]
        private void ClearConsole()
        {
            _consoleBuilder.Clear();
            _lineCount = 0;
            ConsoleOutput = "";
        }

        private void AppendConsoleLine(string line)
        {
            _consoleBuilder.AppendLine(line);
            _lineCount++;

            if (_lineCount > MaxConsoleLines)
            {
                int removeCount = _lineCount - MaxConsoleLines;
                var content = _consoleBuilder.ToString();
                var lines = content.Split('\n');
                if (lines.Length > MaxConsoleLines)
                {
                    var newLines = lines.Skip(lines.Length - MaxConsoleLines);
                    _consoleBuilder.Clear();
                    foreach (var l in newLines)
                    {
                        if (!string.IsNullOrEmpty(l))
                            _consoleBuilder.AppendLine(l);
                    }
                    _lineCount = MaxConsoleLines;
                }
            }

            ConsoleOutput = _consoleBuilder.ToString();
        }
    }

    public class PluginDisplayItem(PluginInfo info)
    {
        public string Name { get; } = info.Name;
        public string Version { get; } = info.Version;
        public string Description { get; } = info.Description;
        public string FileName { get; } = info.FileName;
        public string Authors { get; } = string.Join(", ", info.Authors);
    }
}
