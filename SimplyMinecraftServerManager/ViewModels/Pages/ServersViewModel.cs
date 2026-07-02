// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using SimplyMinecraftServerManager.Internals.Downloads.JDK;
using SimplyMinecraftServerManager.Services;
using SimplyMinecraftServerManager.Views.Pages;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    /// <summary>
    /// 服务器列表页面的视图模型，负责管理 Minecraft 服务器实例的显示、创建、启动、停止和删除等操作。
    /// </summary>
    public partial class ServersViewModel : ObservableObject, INavigationAware, IDisposable
    {
        private readonly IContentDialogService _contentDialogService;
        private readonly INavigationService _navigationService;
        private readonly NavigationParameterService _navigationParameterService;

        /// <summary>
        /// 初始化服务器视图模型的新实例。
        /// </summary>
        /// <param name="contentDialogService">内容对话框服务，用于显示对话框。</param>
        /// <param name="navigationService">导航服务，用于页面跳转。</param>
        /// <param name="navigationParameterService">导航参数服务，用于传递实例 ID。</param>
        public ServersViewModel(IContentDialogService contentDialogService, INavigationService navigationService, NavigationParameterService navigationParameterService)
        {
            _contentDialogService = contentDialogService;
            _navigationService = navigationService;
            _navigationParameterService = navigationParameterService;

            // 订阅全局状态变化事件
            ServerProcessManager.InstanceStatusChanged += OnInstanceStatusChanged;
        }

        /// <summary>
        /// 处理实例运行状态变化事件，更新对应实例的 UI 显示状态。
        /// </summary>
        private void OnInstanceStatusChanged(object? sender, (string InstanceId, bool IsRunning) e)
        {
            RunOnUiThread(() =>
            {
                var item = Instances.FirstOrDefault(i => i.InstanceId == e.InstanceId);
                if (item != null && item.IsRunning != e.IsRunning)
                {
                    item.IsRunning = e.IsRunning;
                    item.IsStarting = false;
                    if (!e.IsRunning)
                    {
                        item.ServerProcess = null;
                    }
                    else
                    {
                        item.ServerProcess = ServerProcessManager.GetProcess(e.InstanceId);
                    }
                }
            });
        }

        /// <summary>
        /// 确保操作在 UI 线程上执行。
        /// </summary>
        /// <param name="action">需要在 UI 线程上执行的操作。</param>
        private static void RunOnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _ = dispatcher.InvokeAsync(action);
        }

[ObservableProperty]
        private ObservableCollection<InstanceDisplayItem> _instances = [];

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _statusMessage = "";

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
        private ObservableCollection<string> _availableMinecraftVersions = [];

        [ObservableProperty]
        private bool _isLoadingVersions = false;

        [ObservableProperty]
        private ObservableCollection<JdkDisplayItem> _availableJdks = [];

        [ObservableProperty]
        private JdkDisplayItem? _selectedJdk;

        // 批量操作
        [ObservableProperty]
        private int _selectedCount = 0;

        /// <summary>
        /// 导航到此页面时执行，加载所有服务器实例、可用 Minecraft 版本和 JDK 列表。
        /// </summary>
        public async Task OnNavigatedToAsync()
        {
            await LoadInstancesAsync();
            await LoadMinecraftVersionsAsync();
            LoadAvailableJdks();
        }

        /// <summary>
        /// 从此页面导航离开时执行。
        /// </summary>
        public Task OnNavigatedFromAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 释放资源，取消订阅实例状态变化事件。
        /// </summary>
        public void Dispose()
        {
            ServerProcessManager.InstanceStatusChanged -= OnInstanceStatusChanged;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 打开新建实例对话框，允许用户创建新的 Minecraft 服务器实例。
        /// </summary>
        [RelayCommand]
        private async Task CreateNewInstanceAsync()
        {
            // 重新加载版本和JDK列表
            await LoadMinecraftVersionsAsync();
            LoadAvailableJdks();

            NewInstanceDialogViewModel? dialogViewModel = null;
            NewInstanceDialog? dialog = null;
            
            dialogViewModel = new NewInstanceDialogViewModel(
                AvailableMinecraftVersions,
                AvailableJdks,
                async () =>
                {
                    if (dialogViewModel != null && dialog != null)
                        await CreateInstanceInternalAsync(dialogViewModel, dialog);
                },
                () => { }
            );

            dialog = new NewInstanceDialog
            {
                DataContext = dialogViewModel
            };

            await _contentDialogService.ShowAsync(dialog, CancellationToken.None);
        }

        /// <summary>
        /// 执行创建实例的内部逻辑，包括验证参数、获取或安装 JDK、下载服务端并创建实例。
        /// </summary>
        /// <param name="vm">新建实例对话框的视图模型，包含用户输入的参数。</param>
        /// <param name="dialog">新建实例对话框实例，用于关闭对话框。</param>
        private async Task CreateInstanceInternalAsync(NewInstanceDialogViewModel vm, NewInstanceDialog dialog)
        {
            InstanceInfo? instance = null;

            if (string.IsNullOrWhiteSpace(vm.InstanceName))
            {
                vm.StatusMessage = "请输入实例名称";
                return;
            }

            if (string.IsNullOrWhiteSpace(vm.SelectedVersion))
            {
                vm.StatusMessage = "请选择 Minecraft 版本";
                return;
            }

            try
            {
                vm.IsCreating = true;
                vm.StatusMessage = "正在准备创建实例...";

                string? javaPath = null;

                if (vm.SelectedJdk != null && File.Exists(vm.SelectedJdk.JavaPath))
                {
                    javaPath = vm.SelectedJdk.JavaPath;
                    vm.StatusMessage = $"正在使用已选择的 JDK {vm.SelectedJdk.MajorVersion}...";
                }
                else
                {
                    int jdkVersion = JdkManager.RecommendJdkVersion(vm.SelectedVersion);
                    javaPath = JdkManager.GetJavaExecutable(jdkVersion);

                    if (string.IsNullOrEmpty(javaPath))
                    {
                        vm.StatusMessage = $"正在安装 JDK {jdkVersion}...";
                        javaPath = await JdkManager.EnsureJdkAsync(jdkVersion);
                    }
                }

                var platform = vm.ServerType switch
                {
                    "Paper" => ServerPlatform.Paper,
                    "Purpur" => ServerPlatform.Purpur,
                    "Leaves" => ServerPlatform.Leaves,
                    "Leaf" => ServerPlatform.Leaf,
                    "Folia" => ServerPlatform.Folia,
                    _ => ServerPlatform.Paper
                };

                var provider = ServerProviderFactory.Get(platform);
                    if (vm.UseCustomJar) {
                        instance = InstanceManager.CreateInstance(
                        name: vm.InstanceName,
                        serverType: vm.ServerType,
                        minecraftVersion: vm.SelectedVersion,
                        jdkPath: javaPath,
                        serverJarSourcePath: vm.CustomJarPath!,
                        minMemoryMb: vm.MinMemory,
                        maxMemoryMb: vm.MaxMemory
                    );
                    Instances.Add(new InstanceDisplayItem(instance));

                    // 关闭对话框并显示成功消息
                    dialog.Hide();
                    StatusMessage = $"实例 \"{instance.Name}\" 创建成功";
                    return;
                }
                var build = await provider.GetLatestBuildAsync(vm.SelectedVersion);

                if (build == null)
                {
                    vm.StatusMessage = "未找到服务端构建";
                    return;
                }

                instance = InstanceManager.CreateInstance(
                    name: vm.InstanceName,
                    serverType: vm.ServerType,
                    minecraftVersion: vm.SelectedVersion,
                    jdkPath: javaPath,
                    serverJar: build.FileName,
                    minMemoryMb: vm.MinMemory,
                    maxMemoryMb: vm.MaxMemory
                );

                vm.StatusMessage = "正在下载服务端...";
                string jarPath = PathHelper.GetServerJarPath(instance.Id, build.FileName);
                await provider.DownloadAsync(build, jarPath);

                Instances.Add(new InstanceDisplayItem(instance));
                
                // 关闭对话框并显示成功消息
                dialog.Hide();
                StatusMessage = $"实例 \"{instance.Name}\" 创建成功";
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

        /// <summary>
        /// 导航到指定服务器实例的详情页面。
        /// </summary>
        /// <param name="item">要查看详情的服务器实例项。</param>
        [RelayCommand]
        private void ViewInstance(InstanceDisplayItem? item)
        {
            if (item == null) return;
            _navigationParameterService.SetInstanceId(item.InstanceId);
            _navigationService.Navigate(typeof(InstancePage));
        }

        /// <summary>
        /// 加载已安装的 JDK 列表，用于在创建实例时选择。
        /// </summary>
        private void LoadAvailableJdks()
        {
            AvailableJdks.Clear();
            var jdks = JdkManager.GetInstalledJdks();
            foreach (var jdk in jdks)
            {
                AvailableJdks.Add(new JdkDisplayItem(jdk));
            }
            if (AvailableJdks.Count > 0)
            {
                SelectedJdk = AvailableJdks[0];
            }
            else
            {
                SelectedJdk = null;
            }
        }

        /// <summary>
        /// 异步加载所有服务器实例，更新实例列表并显示运行状态。
        /// </summary>
        [RelayCommand]
        private async Task LoadInstancesAsync()
        {
            IsLoading = true;
            Instances.Clear();

            try
            {
                ServerProcessManager.CleanupStoppedProcesses();

                var instances = await Task.Run(() => InstanceManager.GetAll().ToList());

                var items = new List<InstanceDisplayItem>();
                foreach (var inst in instances)
                {
                    var item = new InstanceDisplayItem(inst);
                    var process = ServerProcessManager.GetProcess(inst.Id);
                    bool isRunning = process?.IsRunning ?? false;
                    item.ServerProcess = isRunning ? process : null;
                    item.IsRunning = isRunning;
                    items.Add(item);
                }

                foreach (var item in items)
                {
                    Instances.Add(item);
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

        /// <summary>
        /// 异步加载可用的 Minecraft 版本列表，用于创建新实例时选择版本。
        /// </summary>
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
            catch
            {
            }
            finally
            {
                IsLoadingVersions = false;
                if (AvailableMinecraftVersions.Count == 0)
                {
                    StatusMessage = "版本列表加载失败，请稍后重试";
                }
            }
        }

        /// <summary>
        /// 异步删除指定的服务器实例，包括终止运行中的进程和删除所有相关文件。
        /// </summary>
        /// <param name="item">要删除的服务器实例项。</param>
        [RelayCommand]
        private async Task DeleteInstanceAsync(InstanceDisplayItem? item)
        {
            if (item == null) return;

            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除实例 \"{item.Name}\" 吗？\n\n此操作将删除所有相关文件且不可恢复！",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await _contentDialogService.ShowAsync(dialog, CancellationToken.None);

            if (result != ContentDialogResult.Primary) return;

            try
            {
                // 如果服务器正在运行，先强制终止
                ServerProcessManager.KillAndRemove(item.InstanceId);

                InstanceManager.DeleteInstance(item.InstanceId, deleteFiles: true);
                Instances.Remove(item);
                StatusMessage = "实例已删除";
            }
            catch (Exception ex)
            {
                StatusMessage = $"删除失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 异步启动指定的服务器实例。
        /// </summary>
        /// <param name="item">要启动的服务器实例项。</param>
        [RelayCommand]
        private async Task StartInstance(InstanceDisplayItem? item)
        {
            if (item == null || item.IsStarting) return;

            try
            {
                item.IsStarting = true;

                // 检查是否已经在运行
                if (ServerProcessManager.IsRunning(item.InstanceId))
                {
                    StatusMessage = "服务器已在运行";
                    item.IsRunning = true;
                    item.IsStarting = false;
                    return;
                }

                // 清理控制台输出
                item.ClearConsole();
                StatusMessage = "正在启动服务器...";

                // 在后台线程执行启动操作，避免阻塞 UI
                var (process, success, errorMessage) = await Task.Run(() =>
                {
                    try
                    {
                        var proc = new ServerProcess(item.InstanceId);

                        // 订阅事件
                        proc.OutputReceived += (_, line) =>
                        {
                            RunOnUiThread(() =>
                            {
                                item.AppendToConsole(line);
                            });
                        };

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
                    item.IsRunning = false;
                    item.ServerProcess = null;
                    return;
                }

                // 短暂等待，检查是否快速退出
                await Task.Delay(500);

                if (!process.IsRunning)
                {
                    StatusMessage = "服务器启动失败，进程已退出";
                    try { process.Dispose(); } catch { }
                    item.IsRunning = false;
                    item.ServerProcess = null;
                    return;
                }

                // 注册到全局管理器（这会触发 InstanceStatusChanged 事件）
                ServerProcessManager.Register(item.InstanceId, process);

                // 直接更新 UI 状态
                item.ServerProcess = process;
                item.IsRunning = true;
                StatusMessage = "服务器已启动";
            }
            catch (Exception ex)
            {
                item.IsRunning = false;
                item.ServerProcess = null;
                StatusMessage = $"启动失败: {ex.Message}";
            }
            finally
            {
                item.IsStarting = false;
            }
        }

        /// <summary>
        /// 异步停止指定的服务器实例，发送停止信号并等待进程退出。
        /// </summary>
        /// <param name="item">要停止的服务器实例项。</param>
        [RelayCommand]
        private async Task StopInstance(InstanceDisplayItem? item)
        {
            if (item == null) return;

            // 从 ServerProcessManager 获取进程
            var process = ServerProcessManager.GetProcess(item.InstanceId);
            if (process == null || !process.IsRunning)
            {
                // 进程不存在或已停止，同步状态
                if (item.IsRunning)
                {
                    item.IsRunning = false;
                    item.ServerProcess = null;
                }
                StatusMessage = "服务器未在运行";
                return;
            }

            try
            {
                process.Stop();
                StatusMessage = "正在停止服务器...";

                // 等待进程退出（最多10秒）
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (!process.IsRunning) break;
                }

                // 状态更新由 ServerProcessManager 的事件处理
                if (!ServerProcessManager.IsRunning(item.InstanceId))
                {
                    StatusMessage = "服务器已停止";
                }
                else
                {
                    StatusMessage = "服务器停止超时";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"停止失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 强制终止指定的服务器实例，立即杀死进程。
        /// </summary>
        /// <param name="item">要强制终止的服务器实例项。</param>
        [RelayCommand]
        private async Task TerminateInstance(InstanceDisplayItem? item)
        {
            if (item == null) return;

            try
            {
                // 检查进程是否真的在运行
                if (!ServerProcessManager.IsRunning(item.InstanceId))
                {
                    item.IsRunning = false;
                    item.ServerProcess = null;
                    StatusMessage = "服务器未在运行";
                    return;
                }

                ServerProcessManager.KillAndRemove(item.InstanceId);

                // 立即更新 UI 状态
                item.IsRunning = false;
                item.ServerProcess = null;
                StatusMessage = "服务器已被强制终止";

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                StatusMessage = $"终止失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 刷新服务器实例列表。
        /// </summary>
        [RelayCommand]
        private void RefreshInstances()
        {
            _ = LoadInstancesAsync();
        }
    }

    /// <summary>
    /// 服务器实例的显示项，封装实例信息并提供 UI 绑定所需的属性。
    /// </summary>
    public partial class InstanceDisplayItem(InstanceInfo info) : ObservableObject
    {
        private readonly ServerJarMetadata _metadata = ServerJarMetadataReader.Read(info);

        public string InstanceId { get; } = info.Id;
        public string Name { get; } = info.Name;
        public string ServerType => _metadata.ServerType;
        public string MinecraftVersion => _metadata.MinecraftVersion;
        public int MinMemoryMb { get; } = info.MinMemoryMb;
        public int MaxMemoryMb { get; } = info.MaxMemoryMb;

        [ObservableProperty]
        private bool _isStarting;

        [ObservableProperty]
        private bool _isRunning;

        private readonly StringBuilder _consoleBuffer = new(4096);
        private string _cachedConsoleOutput = "";
        private const int MaxConsoleBufferSize = 50000;
        private string? _cachedServerAddress;

        public string ConsoleOutput => _cachedConsoleOutput;

        public void ClearConsole()
        {
            _consoleBuffer.Clear();
            _cachedConsoleOutput = "";
            OnPropertyChanged(nameof(ConsoleOutput));
        }

        public void AppendToConsole(string line)
        {
            _consoleBuffer.Append(line);
            _consoleBuffer.Append('\n');
            if (_consoleBuffer.Length > MaxConsoleBufferSize)
            {
                _consoleBuffer.Remove(0, _consoleBuffer.Length - MaxConsoleBufferSize);
            }
            _cachedConsoleOutput = _consoleBuffer.ToString();
            OnPropertyChanged(nameof(ConsoleOutput));
        }

        public ServerProcess? ServerProcess { get; set; }

        public string StatusText => IsStarting ? "启动中" : IsRunning ? "运行中" : "已停止";

        public string StatusColor => IsStarting ? "#2D8CF0" : IsRunning ? "#4CAF50" : "#9E9E9E";

        public string ServerAddress
        {
            get
            {
                if (_cachedServerAddress != null) return _cachedServerAddress;
                try
                {
                    var props = ServerPropertiesManager.Read(InstanceId);
                    string ip = props.GetValueOrDefault("server-ip", "");
                    string port = props.GetValueOrDefault("server-port", "25565");

                    if (string.IsNullOrWhiteSpace(ip))
                    {
                        ip = "localhost";
                    }

                    _cachedServerAddress = $"{ip}:{port}";
                    return _cachedServerAddress;
                }
                catch
                {
                    _cachedServerAddress = "localhost:25565";
                    return _cachedServerAddress;
                }
            }
        }

        public void InvalidateServerAddressCache() => _cachedServerAddress = null;

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }

        partial void OnIsStartingChanged(bool value)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    /// <summary>
    /// JDK 的显示项，封装已安装 JDK 的信息用于 UI 显示。
    /// </summary>
    public class JdkDisplayItem(InstalledJdk jdk)
    {
        public string DisplayName { get; } = $"{jdk.Distribution} JDK {jdk.MajorVersion} ({jdk.Architecture})";
        public string JavaPath { get; } = jdk.JavaExecutable;
        public int MajorVersion { get; } = jdk.MajorVersion;
        public string Distribution { get; } = jdk.Distribution.ToString();
    }
}
