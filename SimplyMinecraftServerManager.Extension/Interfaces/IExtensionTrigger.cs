// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 扩展触发器接口。
/// 实现此接口的扩展可在指定条件下被宿主触发执行。
/// </summary>
public interface IExtensionTrigger
{
    /// <summary>
    /// 声明此扩展需要的触发器列表。
    /// 宿主在加载扩展时读取并注册这些触发器。
    /// </summary>
    IReadOnlyList<ExtensionTrigger> GetTriggers();

    /// <summary>
    /// 当触发器条件满足时被宿主调用。
    /// </summary>
    /// <param name="context">触发上下文，包含触发时的环境信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>触发执行是否成功</returns>
    Task<bool> OnTriggeredAsync(TriggerContext context, CancellationToken cancellationToken = default);
}
