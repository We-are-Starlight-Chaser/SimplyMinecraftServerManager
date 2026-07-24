// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Reflection;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IExtensionContext 实现。
/// 为扩展提供日志、服务访问、导航注册等能力，并注入 CapabilityGuard 进行安全校验。
/// </summary>
internal sealed class ExtensionContext(
    string extensionId,
    ExtensionCapability capabilities,
    ILogger logger,
    IInstanceService instances,
    IServerService server,
    IDownloadService download,
    IEventBus eventBus,
    IFileService file,
    IFolderService folder,
    string extensionDataPath,
    Version hostSdkVersion,
    ProcessGuard? processGuard = null,
    PInvokeGuard? pInvokeGuard = null,
    NetworkGuard? networkGuard = null,
    ReflectionGuard? reflectionGuard = null,
    SerializationGuard? serializationGuard = null,
    HandleMonitor? handleMonitor = null) : IExtensionContext
{
    private readonly CapabilityGuard _guard = new(capabilities);
    private readonly string _extensionDataPath = extensionDataPath;
    private readonly string _extensionId = extensionId;

    public Version HostSdkVersion { get; } = hostSdkVersion;
    public ILogger Logger { get; } = logger;
    public IInstanceService Instances { get; } = instances;
    public IServerService Server { get; } = server;
    public IDownloadService Download { get; } = download;
    public IEventBus EventBus { get; } = eventBus;
    public IFileService File { get; } = file;
    public IFolderService Folder { get; } = folder;

    /// <summary>进程执行守卫</summary>
    public ProcessGuard? ProcessGuard { get; } = processGuard;

    /// <summary>P/Invoke 调用守卫</summary>
    public PInvokeGuard? PInvokeGuard { get; } = pInvokeGuard;

    /// <summary>网络守卫</summary>
    public NetworkGuard? NetworkGuard { get; } = networkGuard;

    /// <summary>反射守卫</summary>
    public ReflectionGuard? ReflectionGuard { get; } = reflectionGuard;

    /// <summary>序列化守卫</summary>
    public SerializationGuard? SerializationGuard { get; } = serializationGuard;

    /// <summary>句柄监控器</summary>
    public HandleMonitor? HandleMonitor { get; } = handleMonitor;

    /// <summary>安全服务</summary>
    public ISecurityService Security { get; } = new SecurityServiceImpl(
        extensionId, networkGuard, processGuard, pInvokeGuard,
        reflectionGuard, serializationGuard, null, handleMonitor);

    /// <summary>配置存储服务</summary>
    public IConfigService Config { get; } = new ConfigServiceImpl(extensionDataPath);

    /// <summary>定时任务调度服务</summary>
    public ISchedulerService Scheduler { get; } = new SchedulerServiceImpl(extensionId, logger);

    /// <summary>通知服务</summary>
    public INotificationService Notification { get; } = new NotificationServiceImpl(extensionId, logger);

    /// <summary>哈希/校验服务</summary>
    public IHashService Hash { get; } = new HashServiceImpl();

    /// <summary>系统环境信息服务</summary>
    public IEnvironmentService Environment { get; } = new EnvironmentServiceImpl(AppDomain.CurrentDomain.BaseDirectory);

    /// <summary>跨扩展通信服务</summary>
    public IInterExtensionService InterExtension { get; } = new InterExtensionServiceImpl(extensionId, logger);

    /// <summary>
    /// 验证进程创建是否允许。
    /// 扩展在尝试创建进程前应调用此方法。
    /// </summary>
    public bool ValidateProcessCreation(string fileName, string? arguments = null, string? workingDirectory = null)
    {
        if (ProcessGuard is null)
        {
            Logger.Warn($"[{_extensionId}] 进程守卫未初始化，拒绝进程创建");
            return false;
        }

        return ProcessGuard.ValidateProcessCreation(fileName, arguments, workingDirectory);
    }

    /// <summary>
    /// 验证 P/Invoke 调用是否允许。
    /// </summary>
    public bool ValidatePInvokeCall(string libraryName, string functionName)
    {
        if (PInvokeGuard is null)
        {
            Logger.Warn($"[{_extensionId}] P/Invoke 守卫未初始化，拒绝调用");
            return false;
        }

        return PInvokeGuard.ValidatePInvokeCall(libraryName, functionName);
    }

    /// <summary>
    /// 验证网络出站连接是否允许。
    /// </summary>
    public bool ValidateOutboundRequest(string method, string url, string? contentType = null)
    {
        if (NetworkGuard is null)
        {
            Logger.Warn($"[{_extensionId}] 网络守卫未初始化，拒绝网络请求");
            return false;
        }

        return NetworkGuard.ValidateOutbound(method, url, contentType);
    }

    /// <summary>
    /// 验证URL是否安全（防SSRF/DNS重绑定）。
    /// </summary>
    public bool ValidateUrl(string url)
    {
        if (NetworkGuard is null) return false;
        return NetworkGuard.ValidateUrl(url).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 检查反射调用是否被阻止。
    /// </summary>
    public bool IsReflectionCallBlocked(MethodBase? method, string? memberName = null)
    {
        if (ReflectionGuard is null) return false;
        return ReflectionGuard.IsReflectionCallBlocked(method, memberName);
    }

    /// <summary>
    /// 检查序列化调用是否被阻止。
    /// </summary>
    public bool IsSerializationCallBlocked(Type? serializationType, string? methodName = null)
    {
        if (SerializationGuard is null) return false;
        return SerializationGuard.IsSerializationCallBlocked(serializationType, methodName);
    }

    /// <summary>
    /// 检查序列化路径是否安全。
    /// </summary>
    public bool IsSerializationPathBlocked(string filePath)
    {
        if (SerializationGuard is null) return false;
        return SerializationGuard.IsSerializationPathBlocked(filePath);
    }

    /// <summary>
    /// 检查序列化数据是否安全。
    /// </summary>
    public bool IsSerializedDataSafe(byte[] data)
    {
        if (SerializationGuard is null) return true;
        return SerializationGuard.IsSerializedDataSafe(data);
    }

    /// <summary>
    /// 检测当前调用栈是否使用了禁止的API。
    /// </summary>
    public ForbiddenApiDetector.DetectionResult DetectForbiddenApi()
    {
        return ForbiddenApiDetector.DetectFromCallStack(_extensionId);
    }

    /// <summary>
    /// 检查扩展是否可以创建进程。
    /// </summary>
    public bool CanCreateProcess()
    {
        return ProcessGuard?.CanCreateProcess() ?? false;
    }

    /// <summary>
    /// 检查扩展是否可以使用 P/Invoke。
    /// </summary>
    public bool CanUsePInvoke()
    {
        return PInvokeGuard?.CanUsePInvoke() ?? false;
    }

    public void RegisterNavigation(NavigationItemInfo item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _guard.Ensure(ExtensionCapability.Navigation);
    }

    public void UnregisterNavigation(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        _guard.Ensure(ExtensionCapability.Navigation);
    }

    public string GetExtensionDataPath(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        // 安全检查：只能获取自身的数据路径，防止扩展访问其他扩展的数据
        if (!string.Equals(extensionId, _extensionId, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warn($"[{_extensionId}] 尝试访问其他扩展的数据路径: {extensionId}，已拒绝");
            throw new UnauthorizedAccessException($"扩展只能访问自身的数据路径");
        }

        string path = Path.Combine(_extensionDataPath, extensionId);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }
}
