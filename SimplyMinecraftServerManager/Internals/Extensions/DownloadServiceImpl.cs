// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;
using SimplyMinecraftServerManager.Internals.Downloads;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IDownloadService 实现，桥接到 DownloadManager。
/// 所有方法在执行前检查扩展是否拥有 Download 能力。
/// </summary>
internal sealed class DownloadServiceImpl(CapabilityGuard? guard = null) : IDownloadService
{
    private readonly CapabilityGuard? _guard = guard;

    public async Task<bool> DownloadAsync(
        string url,
        string destinationPath,
        IDownloadService.ProgressCallback? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        _guard?.Ensure(ExtensionCapability.Download, "DownloadAsync");

        string directory = Path.GetDirectoryName(destinationPath)!;
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var task = await DownloadManager.Default.EnqueueAsync(
            url, destinationPath, Path.GetFileName(destinationPath)).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<DownloadProgressInfo>? progressHandler = null;
        EventHandler<DownloadTask>? completedHandler = null;
        EventHandler<DownloadTask>? failedHandler = null;

        try
        {
            if (progress is not null)
            {
                progressHandler = (_, e) =>
                {
                    if (e.TaskId == task.Id)
                    {
                        progress(e.BytesDownloaded, e.TotalBytes, e.ProgressPercent);
                    }
                };
                DownloadManager.Default.ProgressChanged += progressHandler;
            }

            completedHandler = (_, e) =>
            {
                if (e.Id == task.Id && e.Status == DownloadStatus.Completed)
                {
                    tcs.TrySetResult(true);
                }
            };
            failedHandler = (_, e) =>
            {
                if (e.Id == task.Id && e.Status == DownloadStatus.Failed)
                {
                    tcs.TrySetResult(false);
                }
            };

            DownloadManager.Default.TaskQueued += completedHandler;
            DownloadManager.Default.TaskFailed += failedHandler;

            // 如果任务已完成/失败，直接检查状态
            if (task.Status == DownloadStatus.Completed) return true;
            if (task.Status is DownloadStatus.Failed or DownloadStatus.Cancelled) return false;

            // 注册取消令牌
            await using var reg = cancellationToken.Register(() =>
            {
                DownloadManager.Default.Cancel(task.Id);
                tcs.TrySetCanceled(cancellationToken);
            });

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (progressHandler is not null)
                DownloadManager.Default.ProgressChanged -= progressHandler;
            if (completedHandler is not null)
                DownloadManager.Default.TaskQueued -= completedHandler;
            if (failedHandler is not null)
                DownloadManager.Default.TaskFailed -= failedHandler;
        }
    }
}
