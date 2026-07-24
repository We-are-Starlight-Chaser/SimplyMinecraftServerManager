// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 扩展上下文，宿主通过此接口向扩展注入各项服务。
/// 每个扩展实例在其生命周期内持有同一个 Context 引用。
///
/// ⚠ 禁止直接使用的 .NET API（项目已有替代方案）：
///   - System.IO.File.* / System.IO.Directory.* → 使用 File / Folder 属性
///   - System.Diagnostics.Process → 使用 Server 属性
///   - System.Net.Http.HttpClient（下载）→ 使用 Download 属性
///   - Console.WriteLine / Debug.WriteLine → 使用 Logger 属性
///   - [DllImport] P/Invoke → 需通过 ValidatePInvokeCall() 校验
/// </summary>
public interface IExtensionContext
{
    /// <summary>当前主程序 SDK 版本</summary>
    Version HostSdkVersion { get; }

    /// <summary>日志服务</summary>
    ILogger Logger { get; }

    /// <summary>实例读取服务</summary>
    IInstanceService Instances { get; }

    /// <summary>
    /// 服务器控制服务（禁止直接使用 System.Diagnostics.Process）。
    /// </summary>
    IServerService Server { get; }

    /// <summary>
    /// 文件下载服务（禁止直接使用 System.Net.Http.HttpClient）。
    /// </summary>
    IDownloadService Download { get; }

    /// <summary>事件总线</summary>
    IEventBus EventBus { get; }

    /// <summary>
    /// 文件操作服务（必须通过此接口访问文件，禁止使用 System.IO）。
    /// </summary>
    IFileService File { get; }

    /// <summary>
    /// 目录操作服务（必须通过此接口访问目录，禁止使用 System.IO）。
    /// </summary>
    IFolderService Folder { get; }

    /// <summary>
    /// 安全服务，供扩展查询自身安全状态和执行安全校验。
    /// </summary>
    ISecurityService Security { get; }

    /// <summary>
    /// 配置存储服务，供扩展安全地读写自身配置。
    /// </summary>
    IConfigService Config { get; }

    /// <summary>
    /// 定时任务调度服务（禁止扩展自行创建 Thread / Timer）。
    /// </summary>
    ISchedulerService Scheduler { get; }

    /// <summary>
    /// 通知服务，供扩展向用户展示通知（禁止直接操作 UI）。
    /// </summary>
    INotificationService Notification { get; }

    /// <summary>
    /// 哈希/校验服务（禁止直接使用 System.Security.Cryptography）。
    /// </summary>
    IHashService Hash { get; }

    /// <summary>
    /// 系统环境信息服务（禁止直接访问 System.Environment / RuntimeInformation）。
    /// </summary>
    IEnvironmentService Environment { get; }

    /// <summary>
    /// 跨扩展通信服务（禁止直接反射调用其他扩展方法）。
    /// </summary>
    IInterExtensionService InterExtension { get; }

    /// <summary>注册自定义导航项到侧边栏</summary>
    void RegisterNavigation(NavigationItemInfo item);

    /// <summary>注销自定义导航项</summary>
    void UnregisterNavigation(string itemId);

    /// <summary>获取扩展专属的配置存储目录路径</summary>
    string GetExtensionDataPath(string extensionId);
}
