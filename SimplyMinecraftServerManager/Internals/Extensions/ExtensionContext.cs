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
internal sealed class ExtensionContext : IExtensionContext
{
    private readonly CapabilityGuard _guard;

    public ExtensionContext(
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
        ProcessGuard? processGuard = null,
        PInvokeGuard? pInvokeGuard = null)
    {
        _guard = new CapabilityGuard(capabilities);
        Logger = logger;
        Instances = instances;
        Server = server;
        Download = download;
        EventBus = eventBus;
        File = file;
        Folder = folder;
        _extensionDataPath = extensionDataPath;
        _extensionId = extensionId;
        ProcessGuard = processGuard;
        PInvokeGuard = pInvokeGuard;
    }

    private readonly string _extensionDataPath;
    private readonly string _extensionId;

    public ILogger Logger { get; }
    public IInstanceService Instances { get; }
    public IServerService Server { get; }
    public IDownloadService Download { get; }
    public IEventBus EventBus { get; }
    public IFileService File { get; }
    public IFolderService Folder { get; }

    /// <summary>进程执行守卫</summary>
    public ProcessGuard? ProcessGuard { get; }

    /// <summary>P/Invoke 调用守卫</summary>
    public PInvokeGuard? PInvokeGuard { get; }

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
