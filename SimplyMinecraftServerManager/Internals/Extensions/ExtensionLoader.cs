// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展加载器：负责发现、加载、初始化、卸载扩展。
/// 使用 AssemblyLoadContext 实现隔离，支持安全校验和生命周期管理。
/// </summary>
internal sealed class ExtensionLoader : IDisposable
{
    private readonly ConcurrentDictionary<string, ExtensionEntry> _extensions = new();
    private readonly ILogger _logger;
    private readonly EventBus _eventBus = new();
    private readonly InstanceServiceImpl _instanceService = new();
    private readonly ServerServiceImpl _serverService = new();
    private readonly DownloadServiceImpl _downloadService = new();
    private readonly string _extensionsPath;
    private readonly string _extensionDataPath;
    private readonly object _lock = new();
    private bool _disposed;

    public TriggerManager TriggerManager { get; }

    public IReadOnlyCollection<string> LoadedExtensionIds => [.. _extensions.Keys];

    /// <summary>
    /// 主程序 SDK 版本，用于扩展兼容性校验。
    /// 从 SimplyMinecraftServerManager.Extension 程序集版本获取。
    /// </summary>
    public Version HostSdkVersion { get; } = GetHostSdkVersion();

    /// <summary>
    /// 单个扩展目录的最大允许大小（字节）。
    /// 超过此大小的扩展将被自动跳过，不加载。
    /// 默认 10MB。
    /// </summary>
    public const long MaxExtensionSizeBytes = 10 * 1024 * 1024;

    public ExtensionLoader(ILogger logger)
    {
        _logger = logger;
        _extensionsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions");
        _extensionDataPath = Path.Combine(PathHelper.Root, "extensionsData");
        TriggerManager = new TriggerManager(logger);

        if (!Directory.Exists(_extensionsPath))
        {
            Directory.CreateDirectory(_extensionsPath);
        }
    }

    /// <summary>
    /// 发现并加载所有扩展。
    /// 流程：扫描目录 → 读取元数据 → 依赖解析 → 逐个加载。
    /// </summary>
    public async Task LoadAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("开始扫描扩展目录...");

        string[] extensionDirs;
        try
        {
            extensionDirs = Directory.GetDirectories(_extensionsPath);
        }
        catch (Exception ex)
        {
            _logger.Error($"扫描扩展目录失败: {_extensionsPath}", ex);
            return;
        }

        if (extensionDirs.Length == 0)
        {
            _logger.Info("未发现任何扩展。");
            return;
        }

        // 第一阶段：发现并读取所有元数据
        var candidates = new Dictionary<string, (string Path, IExtensionMetadata Metadata)>();

        foreach (string dir in extensionDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string extensionId = Path.GetFileName(dir);
            try
            {
                // 检查扩展目录大小
                long dirSize = GetDirectorySize(dir);
                double sizeMB = dirSize / (1024.0 * 1024.0);
                if (dirSize > MaxExtensionSizeBytes)
                {
                    double limitMB = MaxExtensionSizeBytes / (1024.0 * 1024.0);
                    _logger.Warn($"跳过扩展 '{extensionId}': 目录大小 {sizeMB:F1}MB 超过上限 {limitMB:F0}MB");
                    continue;
                }

                var (dllPath, metadata) = await DiscoverExtensionAsync(dir, cancellationToken)
                    .ConfigureAwait(false);
                if (metadata is not null)
                {
                    candidates[extensionId] = (dllPath, metadata);
                    _logger.Info($"发现扩展: {metadata.Name} v{metadata.Version} ({extensionId}) [大小: {sizeMB:F1}MB]");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"跳过扩展 '{extensionId}': {ex.Message}");
            }
        }

        if (candidates.Count == 0)
        {
            _logger.Info("未发现有效的扩展。");
            return;
        }

        // 第二阶段：依赖解析和版本检查
        var metadataDict = candidates.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Metadata);

        _logger.Info($"主程序 SDK 版本: {HostSdkVersion}");

        var resolver = new DependencyResolver(metadataDict, _logger);

        var incompatible = resolver.CheckHostCompatibility(HostSdkVersion);
        foreach (string id in incompatible)
        {
            _logger.Warn($"移除不兼容扩展: {id}");
            candidates.Remove(id);
            metadataDict.Remove(id);
        }

        var invalidVersions = resolver.ValidateDependencyVersions();
        foreach (string id in invalidVersions)
        {
            _logger.Warn($"移除版本不满足的扩展: {id}");
            candidates.Remove(id);
            metadataDict.Remove(id);
        }

        // 第三阶段：按扩展数量决定加载策略
        const int TieredLoadingThreshold = 16;

        if (candidates.Count > TieredLoadingThreshold)
        {
            // 超过 16 个扩展：分层并行加载
            List<List<string>> tiers;
            try
            {
                tiers = resolver.ResolveTiers();
            }
            catch (InvalidOperationException ex)
            {
                _logger.Error($"依赖解析失败: {ex.Message}");
                return;
            }

            _logger.Info($"扩展数量 ({candidates.Count}) 超过 {TieredLoadingThreshold}，启用分层并行加载: " +
                          $"共 {tiers.Count} 层, " +
                          string.Join(", ", tiers.Select((t, i) => $"Tier{i}={t.Count}个")));

            for (int tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
            {
                var tier = tiers[tierIndex];
                cancellationToken.ThrowIfCancellationRequested();

                _logger.Debug($"加载 Tier {tierIndex}: [{string.Join(", ", tier)}]");

                if (tier.Count == 1)
                {
                    string extensionId = tier[0];
                    if (candidates.TryGetValue(extensionId, out var candidate))
                    {
                        await LoadExtensionAsync(extensionId, candidate.Path, candidate.Metadata, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                else
                {
                    var loadTasks = tier.Select(extensionId =>
                    {
                        if (!candidates.TryGetValue(extensionId, out var candidate))
                            return Task.CompletedTask;

                        return LoadExtensionAsync(extensionId, candidate.Path, candidate.Metadata, cancellationToken);
                    });

                    await Task.WhenAll(loadTasks).ConfigureAwait(false);
                }

                _logger.Info($"Tier {tierIndex} 加载完成: {tier.Count} 个扩展");
            }
        }
        else
        {
            // 16 个及以下：顺序加载
            List<string> loadOrder;
            try
            {
                loadOrder = resolver.Resolve();
            }
            catch (InvalidOperationException ex)
            {
                _logger.Error($"依赖解析失败: {ex.Message}");
                return;
            }

            _logger.Info($"扩展数量 ({candidates.Count}) <= {TieredLoadingThreshold}，顺序加载");

            foreach (string extensionId in loadOrder)
            {
                if (!candidates.TryGetValue(extensionId, out var candidate)) continue;

                cancellationToken.ThrowIfCancellationRequested();
                await LoadExtensionAsync(extensionId, candidate.Path, candidate.Metadata, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        _logger.Info($"扩展加载完成: {_extensions.Count} 个扩展已加载。");
    }

    private async Task<(string DllPath, IExtensionMetadata? Metadata)> DiscoverExtensionAsync(
        string extensionDir, CancellationToken cancellationToken)
    {
        string extensionId = Path.GetFileName(extensionDir);
        string dllPath = Path.Combine(extensionDir, $"{extensionId}.dll");

        if (!File.Exists(dllPath))
        {
            return (dllPath, null);
        }

        var loadContext = new ExtensionLoadContext(extensionId);
        try
        {
            // 安全检查：检测扩展是否私自携带了 SDK DLL
            var bundledDlls = loadContext.DetectBundledSdkDlls();
            if (bundledDlls.Count > 0)
            {
                foreach (string bundledDll in bundledDlls)
                {
                    _logger.Warn($"扩展 '{extensionId}' 私自携带了 SDK DLL: {Path.GetFileName(bundledDll)}，" +
                                 "该 DLL 将被忽略，使用主程序提供的 SDK 版本。");
                }
            }

            Assembly assembly = loadContext.LoadFromAssemblyPath(dllPath);

            // 读取 ExtensionAttribute
            var attr = assembly.GetCustomAttribute<ExtensionAttribute>();
            if (attr is null)
            {
                _logger.Warn($"程序集 '{dllPath}' 缺少 [Extension] 特性，跳过。");
                return (dllPath, null);
            }

            // 找到 IExtension 实现
            Type? extensionType = assembly.GetTypes()
                .FirstOrDefault(t =>
                    typeof(IExtension).IsAssignableFrom(t) &&
                    !t.IsAbstract &&
                    !t.IsInterface);

            if (extensionType is null)
            {
                _logger.Warn($"程序集 '{dllPath}' 未找到 IExtension 实现，跳过。");
                return (dllPath, null);
            }

            // 构造元数据
            var metadata = new ExtensionMetadataFromAttribute(attr);
            return (dllPath, metadata);
        }
        catch (Exception ex)
        {
            _logger.Error($"发现扩展失败: {dllPath}", ex);
            loadContext.UnloadExtension();
            return (dllPath, null);
        }
    }

    private async Task LoadExtensionAsync(
        string extensionId,
        string dllPath,
        IExtensionMetadata metadata,
        CancellationToken cancellationToken)
    {
        var lifecycle = new ExtensionLifecycle();
        var entry = new ExtensionEntry
        {
            ExtensionId = extensionId,
            Lifecycle = lifecycle,
            Metadata = metadata
        };

        _extensions[extensionId] = entry;

        try
        {
            // Loading
            lifecycle.TryTransitionTo(ExtensionState.Loading);

            var loadContext = new ExtensionLoadContext(extensionId);
            entry.LoadContext = loadContext;

            Assembly assembly = loadContext.LoadFromAssemblyPath(dllPath);

            Type? extensionType = assembly.GetTypes()
                .FirstOrDefault(t =>
                    typeof(IExtension).IsAssignableFrom(t) &&
                    !t.IsAbstract &&
                    !t.IsInterface) ?? throw new InvalidOperationException($"程序集中未找到 IExtension 实现");

            // 安全校验：检查 RequiredCapabilities
            var instance = (IExtension)Activator.CreateInstance(extensionType)!;
            entry.Extension = instance;
            entry.Capabilities = instance.RequiredCapabilities;

            // 初始化安全防护
            var memGuard = new MemoryGuard(extensionId, new ExtensionLogger(extensionId));
            var integrityChecker = new MemoryIntegrityChecker(extensionId, new ExtensionLogger(extensionId));
            var antiTamper = new AntiTamper(extensionId, _extensionDataPath, new ExtensionLogger(extensionId));
            var processGuard = new ProcessGuard(extensionId, new ExtensionLogger(extensionId));
            var pInvokeGuard = new PInvokeGuard(extensionId, new ExtensionLogger(extensionId));
            var moduleMonitor = new ModuleMonitor(extensionId, new ExtensionLogger(extensionId));
            var networkGuard = new NetworkGuard(extensionId, new ExtensionLogger(extensionId));
            var reflectionGuard = new ReflectionGuard(extensionId, logger: new ExtensionLogger(extensionId));
            var serializationGuard = new SerializationGuard(extensionId, new ExtensionLogger(extensionId));
            var handleMonitor = new HandleMonitor(extensionId, new ExtensionLogger(extensionId));
            var capabilityGuard = new CapabilityGuard(instance.RequiredCapabilities);

            entry.MemoryGuard = memGuard;
            entry.IntegrityChecker = integrityChecker;
            entry.AntiTamper = antiTamper;
            entry.ProcessGuard = processGuard;
            entry.PInvokeGuard = pInvokeGuard;
            entry.ModuleMonitor = moduleMonitor;
            entry.NetworkGuard = networkGuard;
            entry.ReflectionGuard = reflectionGuard;
            entry.SerializationGuard = serializationGuard;
            entry.HandleMonitor = handleMonitor;

            // 记录完整性基线
            integrityChecker.RecordBaseline(extensionType);

            // 注册安全事件
            memGuard.ForcedShutdown += (_, _) =>
            {
                _logger.Error($"[{extensionId}] 内存守卫触发强制终止");
                _ = UnloadAsync(extensionId);
            };

            integrityChecker.ViolationDetected += (_, e) =>
            {
                if (e.IsTerminal)
                {
                    _logger.Error($"[{extensionId}] 完整性违规达上限，强制终止");
                    _ = UnloadAsync(extensionId);
                }
            };

            antiTamper.TamperDetected += (_, e) =>
            {
                if (e.IsTerminal)
                {
                    _logger.Error($"[{extensionId}] 反篡改违规达上限，强制终止");
                    _ = UnloadAsync(extensionId);
                }
            };

            moduleMonitor.ModuleViolation += (_, e) =>
            {
                if (e.IsTerminal)
                {
                    _logger.Error($"[{extensionId}] 模块监控违规达上限，强制终止");
                    _ = UnloadAsync(extensionId);
                }
            };

            networkGuard.ConnectionBlocked += (_, e) =>
            {
                if (e.IsTerminal)
                {
                    _logger.Error($"[{extensionId}] 网络违规达上限，强制终止");
                    _ = UnloadAsync(extensionId);
                }
            };
            
            handleMonitor.CriticalHandleLeak += (_, _) =>
            {
                _logger.Error($"[{extensionId}] 严重句柄泄漏，强制终止");
                _ = UnloadAsync(extensionId);
            };

            _logger.Info($"扩展 '{extensionId}' 声明能力: {instance.RequiredCapabilities}");
            _logger.Info($"扩展 '{extensionId}' 安全防护已启用 (内存=256MB, 完整性=10s, 反篡改=5s, 模块监控=100ms, 进程守卫=启用, P/Invoke守卫=启用, 网络守卫=启用, 反射守卫=启用, 序列化守卫=启用, 句柄监控=启用)");

            // Initializing
            lifecycle.TryTransitionTo(ExtensionState.Initializing);

            // 注入上下文
            var logger = new ExtensionLogger(extensionId);

            // 创建文件访问守卫和文件服务
            IReadOnlyList<FileAccessScope> fileScopes = GetFileScopesFromExtension(instance);
            var fileGuard = new FileAccessGuard(extensionId, logger, fileScopes, _extensionDataPath);
            entry.FileAccessGuard = fileGuard;
            var fileService = new FileServiceImpl(fileGuard, logger);
            var folderService = new FolderServiceImpl(fileGuard, logger);

            // 创建带能力检查的服务实现
            var instanceService = new InstanceServiceImpl(capabilityGuard);
            var serverService = new ServerServiceImpl(capabilityGuard);
            var downloadService = new DownloadServiceImpl(capabilityGuard);

            var context = new ExtensionContext(
                extensionId,
                instance.RequiredCapabilities,
                logger,
                instanceService,
                serverService,
                downloadService,
                _eventBus,
                fileService,
                folderService,
                _extensionDataPath,
                HostSdkVersion,
                processGuard,
                pInvokeGuard);

            instance.SetContext(context);

            // 初始化
            await instance.InitAsync(cancellationToken).ConfigureAwait(false);

            // Ready
            lifecycle.TryTransitionTo(ExtensionState.Ready);

            // 执行
            if (instance.CanExecute())
            {
                lifecycle.TryTransitionTo(ExtensionState.Running);
                await instance.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                _logger.Info($"扩展 '{extensionId}' 执行完成。");
            }
            else
            {
                _logger.Info($"扩展 '{extensionId}' CanExecute 返回 false，跳过执行。");
            }

            // 注册触发器
            if (instance is IExtensionTrigger triggerExtension)
            {
                var triggers = triggerExtension.GetTriggers();
                if (triggers.Count > 0)
                {
                    TriggerManager.Register(extensionId, triggerExtension, triggers);
                    _logger.Info($"扩展 '{extensionId}' 注册了 {triggers.Count} 个触发器");
                }
            }
        }
        catch (Exception ex)
        {
            lifecycle.SetFaulted(ex);
            _logger.Error($"扩展 '{extensionId}' 加载/执行失败: {ex.Message}", ex);
            _extensions.TryRemove(extensionId, out _);
        }
    }

    /// <summary>
    /// 卸载指定扩展
    /// </summary>
    public async Task UnloadAsync(string extensionId)
    {
        if (!_extensions.TryRemove(extensionId, out var entry))
        {
            return;
        }

        try
        {
            entry.Lifecycle.TryTransitionTo(ExtensionState.Disposing);

            TriggerManager.Unregister(extensionId);

            // 释放安全防护
            entry.MemoryGuard?.Dispose();
            entry.IntegrityChecker?.Dispose();
            entry.AntiTamper?.Dispose();
            entry.ProcessGuard?.Dispose();
            entry.PInvokeGuard?.Dispose();
            entry.ModuleMonitor?.Dispose();
            entry.NetworkGuard?.Dispose();
            entry.ReflectionGuard?.Dispose();
            entry.SerializationGuard?.Dispose();
            entry.HandleMonitor?.Dispose();

            entry.Extension?.OnShutdown();
            entry.Extension?.Dispose();

            entry.Lifecycle.TryTransitionTo(ExtensionState.Disposed);
            entry.LoadContext?.UnloadExtension();

            _logger.Info($"扩展 '{extensionId}' 已卸载。");
        }
        catch (Exception ex)
        {
            _logger.Error($"卸载扩展 '{extensionId}' 失败: {ex.Message}", ex);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 卸载所有扩展
    /// </summary>
    public async Task UnloadAllAsync()
    {
        foreach (string extensionId in _extensions.Keys.ToList())
        {
            await UnloadAsync(extensionId).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 向所有已加载扩展广播服务器状态变更
    /// </summary>
    public void NotifyServerStarted(string instanceId)
    {
        foreach (var entry in _extensions.Values)
        {
            if (entry.Lifecycle.State == ExtensionState.Running)
            {
                try { entry.Extension?.OnServerStarted(instanceId); }
                catch (Exception ex) { _logger.Error($"扩展 '{entry.ExtensionId}' OnServerStarted 异常", ex); }
            }
        }
    }

    public void NotifyServerStopped(string instanceId)
    {
        foreach (var entry in _extensions.Values)
        {
            if (entry.Lifecycle.State == ExtensionState.Running)
            {
                try { entry.Extension?.OnServerStopped(instanceId); }
                catch (Exception ex) { _logger.Error($"扩展 '{entry.ExtensionId}' OnServerStopped 异常", ex); }
            }
        }
    }

    public void NotifyPluginInstalled(string instanceId, string pluginName)
    {
        foreach (var entry in _extensions.Values)
        {
            if (entry.Lifecycle.State == ExtensionState.Running)
            {
                try { entry.Extension?.OnPluginInstalled(instanceId, pluginName); }
                catch (Exception ex) { _logger.Error($"扩展 '{entry.ExtensionId}' OnPluginInstalled 异常", ex); }
            }
        }
    }

    public void NotifyConfigChanged(string instanceId, string key, string? value)
    {
        foreach (var entry in _extensions.Values)
        {
            if (entry.Lifecycle.State == ExtensionState.Running)
            {
                try { entry.Extension?.OnConfigChanged(instanceId, key, value); }
                catch (Exception ex) { _logger.Error($"扩展 '{entry.ExtensionId}' OnConfigChanged 异常", ex); }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var entry in _extensions.Values)
        {
            try
            {
                entry.Extension?.OnShutdown();
                entry.Extension?.Dispose();
                entry.LoadContext?.UnloadExtension();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExtensionLoader] Dispose extension '{entry.ExtensionId}' error: {ex.Message}");
            }
        }

        _extensions.Clear();
        TriggerManager.Dispose();
    }

    /// <summary>
    /// 从 SimplyMinecraftServerManager.Extension 程序集获取 SDK 版本。
    /// </summary>
    private static Version GetHostSdkVersion()
    {
        try
        {
            // Extension SDK 程序集应在 Default ALC 中加载
            var sdkAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SimplyMinecraftServerManager.Extension");

            if (sdkAssembly is not null)
            {
                return sdkAssembly.GetName().Version ?? new Version(1, 0, 0);
            }

            // 备选：直接读取 DLL 文件版本
            string sdkDllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SimplyMinecraftServerManager.Extension.dll");
            if (File.Exists(sdkDllPath))
            {
                var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(sdkDllPath);
                return assemblyName.Version ?? new Version(1, 0, 0);
            }
        }
        catch
        {
            // 失败时使用默认版本
        }

        return new Version(1, 0, 0);
    }

    /// <summary>
    /// 计算目录下所有文件的总大小（字节）。
    /// </summary>
    private static long GetDirectorySize(string directory)
    {
        long totalSize = 0;
        try
        {
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    totalSize += new FileInfo(file).Length;
                }
                catch
                {
                    // 文件可能被占用，跳过
                }
            }
        }
        catch
        {
            // 目录可能不可访问
        }
        return totalSize;
    }

    /// <summary>扩展条目</summary>
    private sealed class ExtensionEntry
    {
        public required string ExtensionId { get; init; }
        public required ExtensionLifecycle Lifecycle { get; init; }
        public required IExtensionMetadata Metadata { get; init; }
        public IExtension? Extension { get; set; }
        public ExtensionLoadContext? LoadContext { get; set; }
        public ExtensionCapability Capabilities { get; set; }
        public MemoryGuard? MemoryGuard { get; set; }
        public MemoryIntegrityChecker? IntegrityChecker { get; set; }
        public AntiTamper? AntiTamper { get; set; }
        public ProcessGuard? ProcessGuard { get; set; }
        public PInvokeGuard? PInvokeGuard { get; set; }
        public ModuleMonitor? ModuleMonitor { get; set; }
        public NetworkGuard? NetworkGuard { get; set; }
        public ReflectionGuard? ReflectionGuard { get; set; }
        public SerializationGuard? SerializationGuard { get; set; }
        public HandleMonitor? HandleMonitor { get; set; }
        public FileAccessGuard? FileAccessGuard { get; set; }
    }

    /// <summary>
    /// 默认文件访问范围：每个扩展至少可以访问自己的数据目录（读写）。
    /// 扩展可以通过 IFileScopeProvider 接口声明额外范围。
    /// </summary>
    private static List<FileAccessScope> GetFileScopesFromExtension(IExtension extension)
    {
        var scopes = new List<FileAccessScope>
        {
            new()
            {
                Id = "data",
                Name = "扩展数据目录",
                Level = FileAccessLevel.Full,
                Paths = ["${extensionData}"],
                DeniedExtensions = [".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh"]
            }
        };

        // 如果扩展实现了 IFileScopeProvider，获取额外声明
        if (extension is IFileScopeProvider scopeProvider)
        {
            scopes.AddRange(scopeProvider.DeclareFileScopes());
        }

        return scopes;
    }

    /// <summary>从 ExtensionAttribute 构造 IExtensionMetadata</summary>
    private sealed class ExtensionMetadataFromAttribute(ExtensionAttribute attr) : IExtensionMetadata
    {
        public string Id => attr.Id;
        public string Name => attr.Name;
        public string Description => attr.Description;
        public Version Version => Version.Parse(attr.Version);
        public string[] Authors => attr.Authors;

        public DependencyInfo[] Dependencies => [.. attr.Dependencies.Select(ParseDependency)];

        public Version? HostApiVersion =>
            string.IsNullOrEmpty(attr.HostApiVersion) ? null : Version.Parse(attr.HostApiVersion);

        public string[] Tags => attr.Tags;

        private static DependencyInfo ParseDependency(string dep)
        {
            // 格式: "extensionId" 或 "extensionId>=1.0.0" 或 "extensionId[1.0.0,2.0.0)"
            int idx = dep.IndexOfAny(['[', '(', '>', '=']);
            if (idx < 0)
            {
                return new DependencyInfo(dep, null);
            }

            string id = dep[..idx];
            string versionSpec = dep[idx..];

            // 解析版本范围
            if (versionSpec.StartsWith(">="))
            {
                var ver = Version.Parse(versionSpec[2..]);
                return new DependencyInfo(id, VersionRange.AtLeast(ver));
            }
            if (versionSpec.StartsWith("=="))
            {
                var ver = Version.Parse(versionSpec[2..]);
                return new DependencyInfo(id, VersionRange.Exact(ver));
            }
            if (versionSpec.StartsWith('<'))
            {
                var ver = Version.Parse(versionSpec[1..]);
                return new DependencyInfo(id, VersionRange.Below(ver));
            }
            if (versionSpec.StartsWith('[') || versionSpec.StartsWith('('))
            {
                // 尝试解析 [min,max) 格式
                bool inclusiveMin = versionSpec[0] == '[';
                int comma = versionSpec.IndexOf(',');
                if (comma > 0)
                {
                    string maxPart = versionSpec[(comma + 1)..].TrimEnd(']', ')');
                    bool inclusiveMax = versionSpec[^1] == ']';

                    Version? min = string.IsNullOrWhiteSpace(versionSpec[1..comma])
                        ? null
                        : Version.Parse(versionSpec[1..comma].Trim());
                    Version? max = string.IsNullOrWhiteSpace(maxPart)
                        ? null
                        : Version.Parse(maxPart.Trim());

                    return new DependencyInfo(id, new VersionRange(min, max, inclusiveMin, inclusiveMax));
                }
            }

            return new DependencyInfo(id, null);
        }
    }
}
