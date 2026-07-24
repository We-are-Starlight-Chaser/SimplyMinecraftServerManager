// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 安全的定时任务调度服务。
/// 扩展通过此接口注册定时/延迟任务，由宿主统一调度。
/// 禁止扩展自行创建 Thread / Timer / Task.Delay 循环。
/// </summary>
public interface ISchedulerService
{
    /// <summary>
    /// 注册一次性延迟任务。
    /// </summary>
    /// <param name="delay">延迟时间</param>
    /// <param name="callback">任务回调</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务句柄（可用于取消）</returns>
    Task<string> ScheduleOnceAsync(TimeSpan delay, Func<CancellationToken, Task> callback, CancellationToken ct = default);

    /// <summary>
    /// 注册周期性任务。
    /// </summary>
    /// <param name="interval">执行间隔</param>
    /// <param name="callback">任务回调</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务句柄（可用于取消）</returns>
    Task<string> ScheduleRecurringAsync(TimeSpan interval, Func<CancellationToken, Task> callback, CancellationToken ct = default);

    /// <summary>
    /// 取消已注册的任务。
    /// </summary>
    Task CancelAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 获取当前注册的任务数量（用于安全审计：防止任务泄漏）。
    /// </summary>
    int ActiveTaskCount { get; }
}
