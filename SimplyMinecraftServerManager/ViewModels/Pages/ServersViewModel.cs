using System.Collections.ObjectModel;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class ServersViewModel : ObservableObject, INavigationAware
    {
        [ObservableProperty]
        private ObservableCollection<InstanceDisplayItem> _instances = new();

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _statusMessage = "";

        // 新建实例表单
        [ObservableProperty]
        private string _newInstanceName = "";

        [ObservableProperty]
        private string _newInstanceServerType = "paper";

        [ObservableProperty]
        private string _newInstanceMinecraftVersion = "";

        [ObservableProperty]
        private int _newInstanceMinMemory = 1024;

        [ObservableProperty]
        private int _newInstanceMaxMemory = 2048;

        [ObservableProperty]
        private ObservableCollection<string> _availableMinecraftVersions = new();

        [ObservableProperty]
        private bool _isLoadingVersions = false;

        // 批量操作
        [ObservableProperty]
        private int _selectedCount = 0;

        public async Task OnNavigatedToAsync()
        {
            await LoadInstancesAsync();
            await LoadMinecraftVersionsAsync();
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        [RelayCommand]
        private async Task LoadInstancesAsync()
        {
            IsLoading = true;
            Instances.Clear();

            try
            {
                var instances = InstanceManager.GetAll();
                foreach (var inst in instances)
                {
                    Instances.Add(new InstanceDisplayItem(inst));
                }
                StatusMessage = $"共 {instances.Count} 个实例";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task LoadMinecraftVersionsAsync()
        {
            if (IsLoadingVersions) return;

            IsLoadingVersions = true;
            AvailableMinecraftVersions.Clear();

            try
            {
                var paper = ServerProviderFactory.Get(ServerPlatform.Paper);
                var versions = await paper.GetVersionsAsync();
                foreach (var v in versions.Take(20))
                {
                    AvailableMinecraftVersions.Add(v);
                }
                if (AvailableMinecraftVersions.Count > 0)
                {
                    NewInstanceMinecraftVersion = AvailableMinecraftVersions[0];
                }
            }
            catch { }
            finally
            {
                IsLoadingVersions = false;
            }
        }

        [RelayCommand]
        private async Task CreateNewInstanceAsync()
        {
            if (string.IsNullOrWhiteSpace(NewInstanceName))
            {
                StatusMessage = "请输入实例名称";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewInstanceMinecraftVersion))
            {
                StatusMessage = "请选择 Minecraft 版本";
                return;
            }

            try
            {
                StatusMessage = "正在创建实例...";

                // 获取推荐 JDK
                int jdkVersion = JdkManager.RecommendJdkVersion(NewInstanceMinecraftVersion);
                string? javaPath = JdkManager.GetJavaExecutable(jdkVersion);

                // 如果没有安装 JDK，自动安装
                if (string.IsNullOrEmpty(javaPath))
                {
                    StatusMessage = $"正在安装 JDK {jdkVersion}...";
                    javaPath = await JdkManager.EnsureJdkAsync(jdkVersion);
                }

                // 下载服务端
                var platform = NewInstanceServerType.ToLowerInvariant() switch
                {
                    "paper" => ServerPlatform.Paper,
                    "purpur" => ServerPlatform.Purpur,
                    "leaves" => ServerPlatform.Leaves,
                    "leaf" => ServerPlatform.Leaf,
                    _ => ServerPlatform.Paper
                };

                var provider = ServerProviderFactory.Get(platform);
                var build = await provider.GetLatestBuildAsync(NewInstanceMinecraftVersion);

                if (build == null)
                {
                    StatusMessage = "未找到服务端构建";
                    return;
                }

                // 创建实例
                var instance = InstanceManager.CreateInstance(
                    name: NewInstanceName,
                    serverType: NewInstanceServerType,
                    minecraftVersion: NewInstanceMinecraftVersion,
                    jdkPath: javaPath,
                    serverJar: build.FileName,
                    minMemoryMb: NewInstanceMinMemory,
                    maxMemoryMb: NewInstanceMaxMemory
                );

                // 下载服务端 JAR
                StatusMessage = "正在下载服务端...";
                string jarPath = PathHelper.GetServerJarPath(instance.Id, build.FileName);
                await provider.DownloadAsync(build, jarPath);

                // 添加到列表
                Instances.Add(new InstanceDisplayItem(instance));

                // 清空表单
                NewInstanceName = "";
                StatusMessage = $"实例 \"{instance.Name}\" 创建成功";
            }
            catch (Exception ex)
            {
                StatusMessage = $"创建失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void DeleteInstance(InstanceDisplayItem? item)
        {
            if (item == null) return;

            try
            {
                InstanceManager.DeleteInstance(item.InstanceId, deleteFiles: true);
                Instances.Remove(item);
                StatusMessage = "实例已删除";
            }
            catch (Exception ex)
            {
                StatusMessage = $"删除失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void StartInstance(InstanceDisplayItem? item)
        {
            if (item == null) return;

            try
            {
                if (item.ServerProcess != null && item.ServerProcess.IsRunning)
                {
                    StatusMessage = "服务器已在运行";
                    return;
                }

                item.ServerProcess?.Dispose();
                item.ServerProcess = new ServerProcess(item.InstanceId);

                item.ServerProcess.OutputReceived += (_, line) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.ConsoleOutput += line + "\n";
                    });
                };

                item.ServerProcess.Exited += (_, code) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.IsRunning = false;
                        StatusMessage = $"服务器已退出，代码: {code}";
                    });
                };

                item.ServerProcess.Start();
                item.IsRunning = true;
                StatusMessage = "服务器已启动";
            }
            catch (Exception ex)
            {
                StatusMessage = $"启动失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void StopInstance(InstanceDisplayItem? item)
        {
            if (item == null || item.ServerProcess == null || !item.ServerProcess.IsRunning) return;

            try
            {
                item.ServerProcess.Stop();
                item.IsRunning = false;
                StatusMessage = "正在停止服务器...";
            }
            catch (Exception ex)
            {
                StatusMessage = $"停止失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RefreshInstances()
        {
            _ = LoadInstancesAsync();
        }
    }

    public partial class InstanceDisplayItem : ObservableObject
    {
        public string InstanceId { get; }
        public string Name { get; }
        public string ServerType { get; }
        public string MinecraftVersion { get; }
        public int MinMemoryMb { get; }
        public int MaxMemoryMb { get; }

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private string _consoleOutput = "";

        public ServerProcess? ServerProcess { get; set; }

        public string StatusText => IsRunning ? "运行中" : "已停止";

        public string StatusColor => IsRunning ? "#4CAF50" : "#9E9E9E";

        public InstanceDisplayItem(InstanceInfo info)
        {
            InstanceId = info.Id;
            Name = info.Name;
            ServerType = info.ServerType;
            MinecraftVersion = info.MinecraftVersion;
            MinMemoryMb = info.MinMemoryMb;
            MaxMemoryMb = info.MaxMemoryMb;
        }
    }
}
