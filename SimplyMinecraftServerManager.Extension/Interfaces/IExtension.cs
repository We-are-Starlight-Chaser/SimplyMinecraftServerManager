using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 定义扩展的核心契约。
/// </summary>
public interface IExtension : IDisposable
{
    IExtensionMetadata Metadata { get; }

    /// <summary>
    /// 检查当前环境是否满足执行条件。
    /// 在 ExecuteAsync 之前调用
    /// </summary>
    bool CanExecute();

    /// <summary>
    /// 初始化扩展（注册事件、加载配置等）。
    /// 在所有依赖初始化完成后、CanExecute 之前调用。
    /// </summary>
    Task InitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行扩展的主要逻辑。
    /// 仅在 CanExecute 返回 true 后调用。
    /// </summary>
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}