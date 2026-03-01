using System.Collections.ObjectModel;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class InstanceViewModel : ObservableObject, INavigationAware
    {
        private readonly INavigationService _navigationService;

        [ObservableProperty]
        private string _instanceId = "";

        [ObservableProperty]
        private InstanceInfo? _instanceInfo;

        [ObservableProperty]
        private string _instanceName = "";

        [ObservableProperty]
        private bool _isRunning = false;

        [ObservableProperty]
        private string _consoleOutput = "";

        [ObservableProperty]
        private string _commandInput = "";

        [ObservableProperty]
        private ObservableCollection<PluginDisplayItem> _plugins = new();

        [ObservableProperty]
        private ObservableCollection<KeyValuePair<string, string>> _serverProperties = new();

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

        public InstanceViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
        }

        public async Task OnNavigatedToAsync()
        {
            // 从导航参数获取实例 ID
            await Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        public void LoadInstance(string instanceId)
        {
            InstanceId = instanceId;
            var info = InstanceManager.GetById(instanceId);
            if (info == null)
            {
                StatusMessage = "实例不存在";
                return;
            }

            InstanceInfo = info;
            InstanceName = info.Name;
            EditMinMemory = info.MinMemoryMb.ToString();
            EditMaxMemory = info.MaxMemoryMb.ToString();
            EditJdkPath = info.JdkPath;

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

                _serverProcess.OutputReceived += (_, line) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ConsoleOutput += line + "\n";
                    });
                };

                _serverProcess.ErrorReceived += (_, line) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ConsoleOutput += "[ERR] " + line + "\n";
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
            ConsoleOutput = "";
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
}
