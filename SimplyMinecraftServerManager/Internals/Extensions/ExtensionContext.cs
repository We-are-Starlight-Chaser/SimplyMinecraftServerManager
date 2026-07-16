// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
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
    PInvokeGuard? pInvokeGuard = null) : IExtensionContext
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

    /// <summary>
    /// 验证进程创建是否允许。
    /// 扩展在尝试创建进程前应调用此方法。
    /// </summary>
    /// <param name="fileName">可执行文件名或路径</param>
    /// <param name="arguments">进程参数</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <returns>是否允许创建进程</returns>
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
    /// 扩展在尝试调用 P/Invoke 前应调用此方法。
    /// </summary>
    /// <param name="libraryName">非托管库名称</param>
    /// <param name="functionName">函数名称</param>
    /// <returns>是否允许调用</returns>
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
        _guard.Ensure(ExtensionCapability.Navigation, "RegisterNavigation");
    }

    public void UnregisterNavigation(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        _guard.Ensure(ExtensionCapability.Navigation, "UnregisterNavigation");
    }

    public string GetExtensionDataPath(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        string path = Path.Combine(_extensionDataPath, extensionId);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }
}
