// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展加载器（生产就绪版）。
/// 修复静态节流器 Dispose、Guard 泄漏、串行卸载瓶颈。
/// </summary>
internal sealed class ExtensionLoader : IDisposable
{
    private readonly ConcurrentDictionary<string, ExtensionEntry> _extensions = new();
    private readonly ILogger _logger;
    private readonly EventBus _eventBus = new();
    private readonly string _extensionsPath;
    private readonly string _extensionDataPath;
    private int _disposed;

    // O(1) COW 事件广播
    private volatile List<ExtensionEntry> _runningExtensions = [];
    private readonly Lock _runningListLock = new();

    private readonly SharedModuleMonitor _sharedModuleMonitor;
    public TriggerManager TriggerManager { get; }
    public IReadOnlyCollection<string> LoadedExtensionIds => [.. _extensions.Keys];
    public Version HostSdkVersion { get; } = GetHostSdkVersion();
    public const long MaxExtensionSizeBytes = 10 * 1024 * 1024;

    // 【修复1】改为实例字段，避免 static Dispose 后其他实例崩溃
    private readonly SemaphoreSlim _loadThrottle = new(
        Math.Max(4, Environment.ProcessorCount),
        Math.Max(4, Environment.ProcessorCount));

    public ExtensionLoader(ILogger logger)
    {
        _logger = logger;
        _extensionsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions");
        _extensionDataPath = Path.Combine(PathHelper.Root, "extensionsData");
        TriggerManager = new TriggerManager(logger);
        _sharedModuleMonitor = new SharedModuleMonitor(logger);
        Directory.CreateDirectory(_extensionsPath);
    }

    public async Task LoadAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("开始扫描扩展目录...");

        if (!SystemIntegrityChecker.VerifyWithLogging(_logger))
        {
            _logger.Error("系统完整性校验失败，拒绝加载扩展");
            return;
        }

        string[] extensionDirs;
        try { extensionDirs = Directory.GetDirectories(_extensionsPath); }
        catch (Exception ex) { _logger.Error($"扫描扩展目录失败: {_extensionsPath}", ex); return; }

        if (extensionDirs.Length == 0) { _logger.Info("未发现任何扩展。"); return; }

        // ===== 阶段1：并行发现 =====
        var candidates = new ConcurrentDictionary<string, (string Path, IExtensionMetadata Metadata)>();

        await Parallel.ForEachAsync(extensionDirs, cancellationToken, async (dir, ct) =>
        {
            string extensionId = Path.GetFileName(dir);
            try
            {
                if (IsDirectoryJunctionOrSymlink(dir))
                {
                    _logger.Warn($"跳过 '{extensionId}': Junction/符号链接");
                    return;
                }

                long dirSize = GetDirectorySize(dir); // 【修复4】改回同步，见下方说明
                if (dirSize > MaxExtensionSizeBytes)
                {
                    _logger.Warn($"跳过 '{extensionId}': 大小 {dirSize / (1024.0 * 1024.0):F1}MB 超限");
                    return;
                }

                var (DllPath, Metadata) = await DiscoverExtensionAsync(dir, ct).ConfigureAwait(false);
                if (Metadata is not null)
                {
                    candidates[extensionId] = (DllPath, Metadata);
                    _logger.Info($"发现: {Metadata.Name} v{Metadata.Version} ({extensionId}) [{dirSize / (1024.0 * 1024.0):F1}MB]");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.Warn($"跳过 '{extensionId}': {ex.Message}"); }
        }).ConfigureAwait(false);

        if (candidates.IsEmpty) { _logger.Info("未发现有效扩展。"); return; }

        // ===== 阶段2：依赖解析 =====
        var metadataDict = candidates.ToDictionary(kv => kv.Key, kv => kv.Value.Metadata);
        _logger.Info($"SDK 版本: {HostSdkVersion}");

        var resolver = new DependencyResolver(metadataDict, _logger);

        foreach (string id in resolver.CheckHostCompatibility(HostSdkVersion))
        {
            _logger.Warn($"移除不兼容: {id}");
            candidates.TryRemove(id, out _);
            metadataDict.Remove(id);
        }
        foreach (string id in resolver.ValidateDependencyVersions())
        {
            _logger.Warn($"移除版本不满足: {id}");
            candidates.TryRemove(id, out _);
            metadataDict.Remove(id);
        }

        if (candidates.IsEmpty) { _logger.Info("过滤后无有效扩展。"); return; }

        // ===== 阶段3：加载 =====
        const int Threshold = 16;

        if (candidates.Count > Threshold)
        {
            List<List<string>> tiers;
            try { tiers = resolver.ResolveTiers(); }
            catch (InvalidOperationException ex) { _logger.Error($"依赖解析失败: {ex.Message}"); return; }

            _logger.Info($"分层并行加载: {tiers.Count} 层, " +
                          string.Join(", ", tiers.Select((t, i) => $"T{i}={t.Count}")));

            for (int i = 0; i < tiers.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tier = tiers[i];
                _logger.Debug($"Tier {i}: [{string.Join(", ", tier)}]");

                await Parallel.ForEachAsync(tier, cancellationToken, async (extId, ct) =>
                {
                    if (!candidates.TryGetValue(extId, out var c)) return;
                    await _loadThrottle.WaitAsync(ct).ConfigureAwait(false);
                    try { await LoadExtensionAsync(extId, c.Path, c.Metadata, ct).ConfigureAwait(false); }
                    finally { _loadThrottle.Release(); }
                }).ConfigureAwait(false);

                _logger.Info($"Tier {i} 完成: {tier.Count} 个");
            }
        }
        else
        {
            List<string> order;
            try { order = resolver.Resolve(); }
            catch (InvalidOperationException ex) { _logger.Error($"依赖解析失败: {ex.Message}"); return; }

            _logger.Info($"顺序加载: {order.Count} 个扩展");
            foreach (string id in order)
            {
                if (!candidates.TryGetValue(id, out var c)) continue;
                cancellationToken.ThrowIfCancellationRequested();
                await LoadExtensionAsync(id, c.Path, c.Metadata, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.Info($"加载完成: {_extensions.Count} 个扩展。");
    }

    private async Task<(string DllPath, IExtensionMetadata? Metadata)> DiscoverExtensionAsync(
        string extensionDir, CancellationToken ct)
    {
        string extId = Path.GetFileName(extensionDir);
        string dllPath = Path.Combine(extensionDir, $"{extId}.dll");

        if (!File.Exists(dllPath)) return (dllPath, null);

        // 仅做文件级检查，跳过程序集加载（由 LoadExtensionAsync 统一加载，避免双重加载）
        string metadataPath = Path.Combine(extensionDir, "extension.json");
        if (File.Exists(metadataPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(metadataPath, ct).ConfigureAwait(false);
                var metadata = System.Text.Json.JsonSerializer.Deserialize<JsonExtensionMetadata>(json);
                if (metadata is not null)
                    return (dllPath, metadata);
            }
            catch (Exception ex) { _logger.Warn($"'{extId}' extension.json 解析失败: {ex.Message}"); }
        }

        // 无 extension.json 时，返回占位元数据（Name/Version 等由 LoadExtensionAsync 填充）
        return (dllPath, new PlaceholderMetadata(extId));
    }

    /// <summary>
    /// 加载单个扩展。【修复2】异常路径确保 Guard 资源释放。
    /// </summary>
    private async Task LoadExtensionAsync(
        string extensionId, string dllPath, IExtensionMetadata metadata, CancellationToken ct)
    {
        var lifecycle = new ExtensionLifecycle();
        var entry = new ExtensionEntry
        {
            ExtensionId = extensionId,
            Lifecycle = lifecycle,
            Metadata = metadata
        };

        if (!_extensions.TryAdd(extensionId, entry))
        {
            _logger.Warn($"'{extensionId}' 已加载，跳过");
            return;
        }

        bool guardsCreated = false;
        try
        {
            lifecycle.TryTransitionTo(ExtensionState.Loading);

            var loadContext = new ExtensionLoadContext(extensionId);
            entry.LoadContext = loadContext;

            var bundled = loadContext.DetectBundledSdkDlls();
            foreach (string b in bundled)
                _logger.Warn($"'{extensionId}' 携带 SDK DLL: {Path.GetFileName(b)}，已忽略");

            Assembly asm = loadContext.LoadFromAssemblyPath(dllPath);

            // 如果是占位元数据（无 extension.json），从程序集 [Extension] 属性填充
            if (metadata is PlaceholderMetadata)
            {
                var attr = asm.GetCustomAttribute<Extension.Models.ExtensionAttribute>();
                if (attr is null) { _logger.Warn($"'{dllPath}' 缺少 [Extension]"); _extensions.TryRemove(extensionId, out _); return; }
                metadata = new ExtensionMetadataFromAttribute(attr);
                entry.Metadata = metadata;
            }

            Type extType = asm.GetTypes().FirstOrDefault(t =>
                typeof(IExtension).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                ?? throw new InvalidOperationException("未找到 IExtension 实现");

            var instance = (IExtension)Activator.CreateInstance(extType)!;
            entry.Extension = instance;
            entry.Capabilities = instance.RequiredCapabilities;

            var extLogger = new ExtensionLogger(extensionId);
            var memGuard = new MemoryGuard(extensionId, extLogger);
            var integrityChecker = new MemoryIntegrityChecker(extensionId, extLogger);
            var antiTamper = new AntiTamper(extensionId, _extensionDataPath, extLogger);
            var processGuard = new ProcessGuard(extensionId, extLogger);
            var pInvokeGuard = new PInvokeGuard(extensionId, extLogger);
            var moduleEntry = _sharedModuleMonitor.Register(extensionId);
            var networkGuard = new NetworkGuard(extensionId, extLogger);
            var reflectionGuard = new ReflectionGuard(extensionId, logger: extLogger);
            var serializationGuard = new SerializationGuard(extensionId, extLogger);
            var handleMonitor = new HandleMonitor(extensionId, extLogger);
            var capabilityGuard = new CapabilityGuard(instance.RequiredCapabilities);

            guardsCreated = true;

            loadContext.SetPInvokeGuard(pInvokeGuard);

            entry.MemoryGuard = memGuard;
            entry.IntegrityChecker = integrityChecker;
            entry.AntiTamper = antiTamper;
            entry.ProcessGuard = processGuard;
            entry.PInvokeGuard = pInvokeGuard;
            entry.ModuleMonitorEntry = moduleEntry;
            entry.NetworkGuard = networkGuard;
            entry.ReflectionGuard = reflectionGuard;
            entry.SerializationGuard = serializationGuard;
            entry.HandleMonitor = handleMonitor;

            integrityChecker.RecordBaseline(extType);

            void OnTerminalViolation(string guardName)
            {
                _logger.Error($"[{extensionId}] {guardName} 违规达上限，强制终止");
                _ = UnloadAsync(extensionId);
            }

            memGuard.ForcedShutdown += (_, _) => OnTerminalViolation("内存守卫");
            integrityChecker.ViolationDetected += (_, e) => { if (e.IsTerminal) OnTerminalViolation("完整性"); };
            antiTamper.TamperDetected += (_, e) => { if (e.IsTerminal) OnTerminalViolation("反篡改"); };
            moduleEntry.ModuleViolation += (_, e) => { if (e.IsTerminal) OnTerminalViolation("模块监控"); };
            networkGuard.ConnectionBlocked += (_, e) => { if (e.IsTerminal) OnTerminalViolation("网络"); };
            handleMonitor.CriticalHandleLeak += (_, _) => OnTerminalViolation("句柄泄漏");

            _logger.Info($"'{extensionId}' 能力: {instance.RequiredCapabilities}");

            lifecycle.TryTransitionTo(ExtensionState.Initializing);

            var fileScopes = GetFileScopesFromExtension(instance);
            var fileGuard = new FileAccessGuard(extensionId, extLogger, fileScopes, _extensionDataPath);
            entry.FileAccessGuard = fileGuard;
            var fileService = new FileServiceImpl(fileGuard, extLogger, extensionId, handleMonitor, reflectionGuard, serializationGuard);
            var folderService = new FolderServiceImpl(fileGuard, extLogger, extensionId, reflectionGuard, serializationGuard);

            var context = new ExtensionContext(
                extensionId, instance.RequiredCapabilities, extLogger,
                new InstanceServiceImpl(capabilityGuard),
                new ServerServiceImpl(capabilityGuard, processGuard, handleMonitor),
                new DownloadServiceImpl(capabilityGuard, networkGuard),
                _eventBus, fileService, folderService, _extensionDataPath,
                HostSdkVersion, processGuard, pInvokeGuard, networkGuard,
                reflectionGuard, serializationGuard, handleMonitor);

            instance.SetContext(context);
            await instance.InitAsync(ct).ConfigureAwait(false);
            antiTamper.StartMonitoring();

            lifecycle.TryTransitionTo(ExtensionState.Ready);

            if (instance.CanExecute())
            {
                lifecycle.TryTransitionTo(ExtensionState.Running);
                AddToRunningList(entry);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                try
                {
                    await instance.ExecuteAsync(linkedCts.Token).ConfigureAwait(false);
                    _logger.Info($"'{extensionId}' 执行完成");
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    _logger.Error($"'{extensionId}' 执行超时(5min)");
                    lifecycle.SetFaulted(new TimeoutException("执行超时"));
                    RemoveFromRunningList(entry);
                }
            }
            else
            {
                _logger.Info($"'{extensionId}' CanExecute=false，跳过执行");
            }

            if (instance is IExtensionTrigger trigger)
            {
                var triggers = trigger.GetTriggers();
                if (triggers.Count > 0)
                {
                    TriggerManager.Register(extensionId, trigger, triggers);
                    _logger.Info($"'{extensionId}' 注册 {triggers.Count} 个触发器");
                }
            }
        }
        catch (Exception ex)
        {
            lifecycle.SetFaulted(ex);
            _logger.Error($"'{extensionId}' 加载失败: {ex.Message}", ex);
            if (guardsCreated)
            {
                entry.MemoryGuard?.Dispose();
                entry.IntegrityChecker?.Dispose();
                entry.AntiTamper?.Dispose();
                entry.ProcessGuard?.Dispose();
                entry.PInvokeGuard?.Dispose();
                entry.ModuleMonitorEntry?.Dispose();
                entry.NetworkGuard?.Dispose();
                entry.ReflectionGuard?.Dispose();
                entry.SerializationGuard?.Dispose();
                entry.HandleMonitor?.Dispose();
                entry.FileAccessGuard?.Dispose();
                entry.LoadContext?.UnloadExtension();
            }

            _extensions.TryRemove(extensionId, out _);
        }
    }

    // ===== COW 事件广播 =====
    private void AddToRunningList(ExtensionEntry entry)
    {
        lock (_runningListLock)
        {
            var newList = new List<ExtensionEntry>(_runningExtensions.Count + 1);
            newList.AddRange(_runningExtensions);
            newList.Add(entry);
            _runningExtensions = newList;
        }
    }

    private void RemoveFromRunningList(ExtensionEntry entry)
    {
        lock (_runningListLock)
        {
            var newList = _runningExtensions.Where(e => e != entry).ToList();
            _runningExtensions = newList;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BroadcastToRunning(Action<IExtension> action)
    {
        var snapshot = _runningExtensions;
        foreach (var entry in snapshot)
        {
            try { action(entry.Extension!); }
            catch (Exception ex) { _logger.Error($"'{entry.ExtensionId}' 广播异常", ex); }
        }
    }

    public void NotifyServerStarted(string instanceId) =>
        BroadcastToRunning(e => e.OnServerStarted(instanceId));

    public void NotifyServerStopped(string instanceId) =>
        BroadcastToRunning(e => e.OnServerStopped(instanceId));

    public void NotifyPluginInstalled(string instanceId, string pluginName) =>
        BroadcastToRunning(e => e.OnPluginInstalled(instanceId, pluginName));

    public void NotifyConfigChanged(string instanceId, string key, string? value) =>
        BroadcastToRunning(e => e.OnConfigChanged(instanceId, key, value));

    // ===== 卸载 =====
    public async Task UnloadAsync(string extensionId)
    {
        if (!_extensions.TryRemove(extensionId, out var entry)) return;

        try
        {
            entry.Lifecycle.TryTransitionTo(ExtensionState.Disposing);
            RemoveFromRunningList(entry);
            TriggerManager.Unregister(extensionId);

            entry.MemoryGuard?.Dispose();
            entry.IntegrityChecker?.Dispose();
            entry.AntiTamper?.Dispose();
            entry.ProcessGuard?.Dispose();
            entry.PInvokeGuard?.Dispose();
            _sharedModuleMonitor.Unregister(extensionId);
            entry.NetworkGuard?.Dispose();
            entry.ReflectionGuard?.Dispose();
            entry.SerializationGuard?.Dispose();
            entry.HandleMonitor?.Dispose();
            entry.FileAccessGuard?.Dispose();
            entry.ModuleMonitorEntry?.Dispose();
            entry.Extension?.OnShutdown();
            entry.Extension?.Dispose();

            entry.Lifecycle.TryTransitionTo(ExtensionState.Disposed);
            entry.LoadContext?.UnloadExtension();
            _logger.Info($"'{extensionId}' 已卸载");
        }
        catch (Exception ex) { _logger.Error($"卸载 '{extensionId}' 失败: {ex.Message}", ex); }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 【修复3】并行卸载 + 超时保护，避免单个扩展卡死阻塞全部。
    /// </summary>
    public async Task UnloadAllAsync()
    {
        var ids = _extensions.Keys.ToList();
        if (ids.Count == 0) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Parallel.ForEachAsync(ids, cts.Token, async (id, ct) =>
        {
            try { await UnloadAsync(id).ConfigureAwait(false); }
            catch (Exception ex) { _logger.Error($"批量卸载 '{id}' 失败: {ex.Message}"); }
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        foreach (var entry in _extensions.Values)
        {
            try
            {
                entry.Lifecycle.TryTransitionTo(ExtensionState.Disposing);
                entry.MemoryGuard?.Dispose();
                entry.IntegrityChecker?.Dispose();
                entry.AntiTamper?.Dispose();
                entry.ProcessGuard?.Dispose();
                entry.PInvokeGuard?.Dispose();
                entry.NetworkGuard?.Dispose();
                entry.ReflectionGuard?.Dispose();
                entry.SerializationGuard?.Dispose();
                entry.HandleMonitor?.Dispose();
                entry.ModuleMonitorEntry?.Dispose();
                entry.FileAccessGuard?.Dispose();
                entry.Extension?.OnShutdown();
                entry.Extension?.Dispose();
                entry.Lifecycle.TryTransitionTo(ExtensionState.Disposed);
                entry.LoadContext?.UnloadExtension();
            }
            catch (Exception ex) { Debug.WriteLine($"[ExtLoader] Dispose '{entry.ExtensionId}': {ex.Message}"); }
        }
        _extensions.Clear();
        _runningExtensions = [];
        TriggerManager.Dispose();
        _sharedModuleMonitor.Dispose();
        _loadThrottle.Dispose(); // 现在是实例字段，安全 Dispose
    }

    // ===== 工具方法 =====
    private static Version GetHostSdkVersion()
    {
        try
        {
            var sdkAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SimplyMinecraftServerManager.Extension");
            if (sdkAsm is not null) return sdkAsm.GetName().Version ?? new Version(1, 0, 0);

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SimplyMinecraftServerManager.Extension.dll");
            if (File.Exists(path)) return AssemblyName.GetAssemblyName(path).Version ?? new Version(1, 0, 0);
        }
        catch { /* fallback */ }
        return new Version(1, 0, 0);
    }

    /// <summary>
    /// 【修复4】同步计算目录大小。
    /// Directory.GetFiles 本身是同步 API，包装成 async 只是欺骗编译器。
    /// Parallel.ForEachAsync 外层已提供并行度，此处保持简单可靠。
    /// </summary>
    private static long GetDirectorySize(string directory)
    {
        long total = 0;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
            };
            foreach (string file in Directory.EnumerateFiles(directory, "*", options))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* 文件被占用或权限不足 */ }
            }
        }
        catch { /* 目录不可访问 */ }
        return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDirectoryJunctionOrSymlink(string directory)
    {
        try { return (new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    private static List<FileAccessScope> GetFileScopesFromExtension(IExtension extension)
    {
        var scopes = new List<FileAccessScope>
        {
            new()
            {
                Id = "data", Name = "扩展数据目录", Level = FileAccessLevel.Full,
                Paths = ["${extensionData}"],
                DeniedExtensions = [".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh"]
            }
        };
        if (extension is IFileScopeProvider sp) scopes.AddRange(sp.DeclareFileScopes());
        return scopes;
    }

    private sealed class ExtensionEntry
    {
        public required string ExtensionId { get; init; }
        public required ExtensionLifecycle Lifecycle { get; init; }
        public required IExtensionMetadata Metadata { get; set; }
        public IExtension? Extension { get; set; }
        public ExtensionLoadContext? LoadContext { get; set; }
        public ExtensionCapability Capabilities { get; set; }
        public MemoryGuard? MemoryGuard { get; set; }
        public MemoryIntegrityChecker? IntegrityChecker { get; set; }
        public AntiTamper? AntiTamper { get; set; }
        public ProcessGuard? ProcessGuard { get; set; }
        public PInvokeGuard? PInvokeGuard { get; set; }
        public SharedModuleMonitor.ModuleMonitorEntry? ModuleMonitorEntry { get; set; }
        public NetworkGuard? NetworkGuard { get; set; }
        public ReflectionGuard? ReflectionGuard { get; set; }
        public SerializationGuard? SerializationGuard { get; set; }
        public HandleMonitor? HandleMonitor { get; set; }
        public FileAccessGuard? FileAccessGuard { get; set; }
    }

    /// <summary>
    /// 从 extension.json 解析的元数据。
    /// </summary>
    private sealed class JsonExtensionMetadata : IExtensionMetadata
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public Version Version { get; set; } = new(1, 0, 0);
        public string[] Authors { get; set; } = [];
        public DependencyInfo[] Dependencies { get; set; } = [];
        public Version? HostApiVersion { get; set; }
        public string[] Tags { get; set; } = [];
    }

    /// <summary>
    /// 占位元数据，表示发现阶段未加载程序集，由加载阶段从 [Extension] 属性填充。
    /// </summary>
    private sealed class PlaceholderMetadata(string extensionId) : IExtensionMetadata
    {
        public string Id => extensionId;
        public string Name => extensionId;
        public string Description => "";
        public Version Version => new(1, 0, 0);
        public string[] Authors => [];
        public DependencyInfo[] Dependencies => [];
        public Version? HostApiVersion => null;
        public string[] Tags => [];
    }

    private sealed class ExtensionMetadataFromAttribute(Extension.Models.ExtensionAttribute attr) : IExtensionMetadata
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
            int idx = dep.IndexOfAny(['[', '(', '>', '=']);
            if (idx < 0) return new DependencyInfo(dep, null);

            string id = dep[..idx];
            string spec = dep[idx..];

            if (spec.StartsWith(">=")) return new DependencyInfo(id, VersionRange.AtLeast(Version.Parse(spec[2..])));
            if (spec.StartsWith("==")) return new DependencyInfo(id, VersionRange.Exact(Version.Parse(spec[2..])));
            if (spec.StartsWith('<')) return new DependencyInfo(id, VersionRange.Below(Version.Parse(spec[1..])));

            if (spec.StartsWith('[') || spec.StartsWith('('))
            {
                bool incMin = spec[0] == '[';
                int comma = spec.IndexOf(',');
                if (comma > 0)
                {
                    string maxPart = spec[(comma + 1)..].TrimEnd(']', ')');
                    bool incMax = spec[^1] == ']';
                    Version? min = string.IsNullOrWhiteSpace(spec[1..comma]) ? null : Version.Parse(spec[1..comma].Trim());
                    Version? max = string.IsNullOrWhiteSpace(maxPart) ? null : Version.Parse(maxPart.Trim());
                    return new DependencyInfo(id, new VersionRange(min, max, incMin, incMax));
                }
            }
            return new DependencyInfo(id, null);
        }
    }
}