// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// ISchedulerService 实现，基于 CancellationTokenSource 管理定时任务。
/// 每个扩展独立限额，防止任务泄漏。
/// </summary>
internal sealed class SchedulerServiceImpl(string extensionId, ILogger logger) : ISchedulerService
{
    private readonly string _extensionId = extensionId;
    private readonly ILogger _logger = logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tasks = new();
    private const int MaxActiveTasks = 50;

    public int ActiveTaskCount => _tasks.Count;

    public async Task<string> ScheduleOnceAsync(TimeSpan delay, Func<CancellationToken, Task> callback, CancellationToken ct = default)
    {
        if (_tasks.Count >= MaxActiveTasks)
        {
            _logger.Warn($"[{_extensionId}] 定时任务数已达上限 {MaxActiveTasks}，拒绝新任务");
            throw new InvalidOperationException($"定时任务数已达上限 {MaxActiveTasks}");
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var taskId = Guid.NewGuid().ToString("N")[..12];
        _tasks[taskId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                await callback(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Error($"[{_extensionId}] 定时任务 {taskId} 执行失败", ex);
            }
            finally
            {
                _tasks.TryRemove(taskId, out _);
                cts.Dispose();
            }
        }, CancellationToken.None);

        return await Task.FromResult(taskId).ConfigureAwait(false);
    }

    public async Task<string> ScheduleRecurringAsync(TimeSpan interval, Func<CancellationToken, Task> callback, CancellationToken ct = default)
    {
        if (_tasks.Count >= MaxActiveTasks)
        {
            _logger.Warn($"[{_extensionId}] 定时任务数已达上限 {MaxActiveTasks}，拒绝新任务");
            throw new InvalidOperationException($"定时任务数已达上限 {MaxActiveTasks}");
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var taskId = Guid.NewGuid().ToString("N")[..12];
        _tasks[taskId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(interval, cts.Token).ConfigureAwait(false);
                    try
                    {
                        await callback(cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[{_extensionId}] 周期任务 {taskId} 执行失败", ex);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _tasks.TryRemove(taskId, out _);
                cts.Dispose();
            }
        }, CancellationToken.None);

        return await Task.FromResult(taskId).ConfigureAwait(false);
    }

    public Task CancelAsync(string taskId, CancellationToken ct = default)
    {
        if (_tasks.TryRemove(taskId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        return Task.CompletedTask;
    }
}
