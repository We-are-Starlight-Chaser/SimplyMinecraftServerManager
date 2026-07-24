// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 触发器管理器：管理所有扩展触发器的注册、条件求值和异步执行。
/// 线程安全，支持一次性触发器和定时触发器。
/// </summary>
internal sealed class TriggerManager(ILogger logger) : IDisposable
{
    private readonly ConcurrentDictionary<string, RegisteredTrigger> _triggers = new();
    private readonly ConcurrentDictionary<string, Timer> _timers = new();
    private readonly ILogger _logger = logger;
    private bool _disposed;

    /// <summary>
    /// 注册扩展的触发器。
    /// </summary>
    public void Register(string extensionId, IExtensionTrigger triggerExtension, IReadOnlyList<ExtensionTrigger> triggers)
    {
        foreach (var trigger in triggers)
        {
            string triggerKey = $"{extensionId}:{trigger.Type}:{_triggers.Count}";

            var registered = new RegisteredTrigger
            {
                ExtensionId = extensionId,
                Trigger = trigger,
                Extension = triggerExtension
            };

            _triggers[triggerKey] = registered;

            // 定时触发器：创建 Timer
            if (trigger.Type.HasFlag(TriggerType.Timer))
            {
                SetupTimer(triggerKey, registered);
            }

            _logger.Debug($"注册触发器: {triggerKey} (Type={trigger.Type}, Once={trigger.Once})");
        }
    }

    /// <summary>
    /// 注销指定扩展的所有触发器。
    /// </summary>
    public void Unregister(string extensionId)
    {
        foreach (var kv in _triggers)
        {
            if (kv.Value.ExtensionId == extensionId)
            {
                _triggers.TryRemove(kv.Key, out _);
            }
        }

        // 清理定时器
        foreach (var kv in _timers)
        {
            if (kv.Key.StartsWith(extensionId, StringComparison.OrdinalIgnoreCase))
            {
                if (_timers.TryRemove(kv.Key, out var timer))
                {
                    timer.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 当事件发生时，检查并执行匹配的触发器。
    /// </summary>
    public async Task FireAsync(TriggerType type, TriggerContext context, CancellationToken cancellationToken = default)
    {
        // 收集匹配的触发器，按优先级排序
        var matched = _triggers.Values
            .Where(t => TriggerEvaluator.Evaluate(t.Trigger, context))
            .OrderBy(t => t.Trigger.Priority)
            .ToList();

        foreach (var registered in matched)
        {
            if (_disposed || cancellationToken.IsCancellationRequested) break;

            try
            {
                _logger.Debug($"触发器执行: {registered.ExtensionId} (Type={type}, Source={context.Source})");

                bool success = await registered.Extension
                    .OnTriggeredAsync(context, cancellationToken)
                    .ConfigureAwait(false);

                // 一次性触发器：执行后注销
                if (registered.Trigger.Once)
                {
                    string? key = _triggers.FirstOrDefault(kv => kv.Value == registered).Key;
                    if (key is not null)
                    {
                        _triggers.TryRemove(key, out _);
                        _logger.Debug($"一次性触发器已注销: {key}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"触发器执行异常: {registered.ExtensionId}", ex);
            }
        }
    }

    private void SetupTimer(string triggerKey, RegisteredTrigger registered)
    {
        var trigger = registered.Trigger;

        // 从参数中解析间隔
        TimeSpan interval = TimeSpan.FromMinutes(5); // 默认 5 分钟

        if (trigger.Parameters.TryGetValue("interval", out string? intervalStr))
        {
            if (!TryParseInterval(intervalStr, out interval))
            {
                _logger.Warn($"无法解析定时器间隔: {intervalStr}，使用默认 5 分钟");
                interval = TimeSpan.FromMinutes(5);
            }
        }

        var timer = new Timer(
            callback: _ => _ = OnTimerElapsed(triggerKey, registered),
            state: null,
            dueTime: interval,
            period: interval);

        _timers[triggerKey] = timer;
        _logger.Debug($"定时触发器已启动: {triggerKey}, 间隔={interval}");
    }

    private async Task OnTimerElapsed(string triggerKey, RegisteredTrigger registered)
    {
        if (_disposed) return;

        var context = new TriggerContext
        {
            TriggerType = TriggerType.Timer,
            Source = $"timer:{triggerKey}",
            InstanceId = registered.Trigger.Parameters.TryGetValue("instanceId", out var id) ? id : null
        };

        await FireAsync(TriggerType.Timer, context).ConfigureAwait(false);
    }

    // 最小触发间隔（防止恶意扩展高频触发）
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(5);

    private static bool TryParseInterval(string input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        string trimmed = input.Trim().ToLowerInvariant();

        // 尝试解析 "5m", "1h", "30s", "2h30m" 等格式
        if (trimmed.EndsWith('s') && int.TryParse(trimmed[..^1], out int seconds))
        {
            result = TimeSpan.FromSeconds(seconds);
        }
        else if (trimmed.EndsWith('m') && int.TryParse(trimmed[..^1], out int minutes))
        {
            result = TimeSpan.FromMinutes(minutes);
        }
        else if (trimmed.EndsWith('h') && int.TryParse(trimmed[..^1], out int hours))
        {
            result = TimeSpan.FromHours(hours);
        }
        else
        {
            // 尝试 TimeSpan.Parse
            if (!TimeSpan.TryParse(input, out result))
            {
                return false;
            }
        }

        // 最小间隔限制
        if (result < MinInterval)
        {
            result = MinInterval;
        }

        return result > TimeSpan.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kv in _timers)
        {
            kv.Value.Dispose();
        }
        _timers.Clear();
        _triggers.Clear();
    }

    private sealed class RegisteredTrigger
    {
        public required string ExtensionId { get; init; }
        public required ExtensionTrigger Trigger { get; init; }
        public required IExtensionTrigger Extension { get; init; }
    }
}
