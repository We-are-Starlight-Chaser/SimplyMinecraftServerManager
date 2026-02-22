using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    public sealed class DownloadManager : IDisposable
    {
        private static readonly object _defaultLock = new();
        private static DownloadManager? _default;

        /// <summary>
        /// 全局默认实例（首次访问自动读取 AppConfig.DownloadThreads）
        /// </summary>
        public static DownloadManager Default
        {
            get
            {
                if (_default == null)
                {
                    lock (_defaultLock)
                    {
                        _default ??= new DownloadManager(
                            ConfigManager.Current.DownloadThreads);
                    }
                }
                return _default;
            }
        }

        /// <summary>
        /// 用新线程数重建全局默认实例。
        /// 不影响旧实例上仍在运行的任务（它们会正常完成）。
        /// </summary>
        public static void ReconfigureDefault(int maxConcurrentDownloads)
        {
            lock (_defaultLock)
            {
                // 旧实例不 Dispose：正在执行的任务仍持有旧 semaphore 引用，
                // 让它们自然完成后 GC 回收即可。
                _default = new DownloadManager(maxConcurrentDownloads);
            }
        }

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly ConcurrentDictionary<string, DownloadTask> _tasks = new();
        private const int BufferSize = 81920;

        // 可热替换的 Semaphore
        private volatile SemaphoreSlim _semaphore;
        private volatile int _maxConcurrent;

        public event EventHandler<DownloadProgressInfo>? ProgressChanged;
        public event EventHandler<DownloadTask>? TaskCompleted;
        public event EventHandler<DownloadTask>? TaskFailed;

        /// <summary>当前最大并发数。</summary>
        public int MaxConcurrentDownloads => _maxConcurrent;

        public DownloadManager(int maxConcurrentDownloads = 4, HttpClient? httpClient = null)
        {
            _maxConcurrent = Math.Clamp(maxConcurrentDownloads, 1, 32);
            _semaphore = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);

            if (httpClient != null)
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
            }
            else
            {
                var handler = new HttpClientHandler();
                _httpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromMinutes(30)
                };
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "SimplyMinecraftServerManager/1.0 (Windows)");
                _httpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");
                _ownsHttpClient = true;
            }
        }

        // 动态调整并发数

        /// <summary>
        /// 动态修改最大并发下载数。
        /// 已在运行的任务不受影响，新提交的任务立即使用新限制。
        /// </summary>
        public void SetMaxConcurrentDownloads(int value)
        {
            value = Math.Clamp(value, 1, 32);
            if (value == _maxConcurrent) return;

            _maxConcurrent = value;
            // 创建新信号量，旧信号量被正在运行的任务引用，完成后自然 GC
            _semaphore = new SemaphoreSlim(value, value);
        }

        // 查询

        public IReadOnlyList<DownloadTask> AllTasks => _tasks.Values.ToList().AsReadOnly();
        public int ActiveCount => _tasks.Values.Count(t => t.Status == DownloadStatus.Downloading);

        // 入队

        public Task<DownloadTask> EnqueueAsync(DownloadTask downloadTask)
        {
            _tasks[downloadTask.Id] = downloadTask;
            // 捕获当前 semaphore 引用，保证任务全生命周期使用同一个
            var semaphore = _semaphore;
            return Task.Run(() => ExecuteAsync(downloadTask, semaphore));
        }

        public Task<DownloadTask> EnqueueAsync(
            string url,
            string destinationPath,
            string displayName = "",
            string? expectedHash = null,
            string hashAlgorithm = "SHA256")
        {
            var task = new DownloadTask
            {
                Url = url,
                DestinationPath = destinationPath,
                DisplayName = string.IsNullOrEmpty(displayName)
                    ? Path.GetFileName(destinationPath)
                    : displayName,
                ExpectedHash = expectedHash,
                HashAlgorithm = hashAlgorithm
            };
            return EnqueueAsync(task);
        }

        public async Task<DownloadTask[]> EnqueueBatchAsync(IEnumerable<DownloadTask> tasks)
        {
            var running = tasks.Select(t => EnqueueAsync(t)).ToArray();
            return await Task.WhenAll(running);
        }

        // 取消

        public void Cancel(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.Cts.Cancel();
                task.Status = DownloadStatus.Cancelled;
            }
        }

        public void CancelAll()
        {
            foreach (var task in _tasks.Values)
            {
                task.Cts.Cancel();
                if (task.Status is DownloadStatus.Pending or DownloadStatus.Downloading)
                    task.Status = DownloadStatus.Cancelled;
            }
        }

        public void ClearFinished()
        {
            var toRemove = _tasks.Values
                .Where(t => t.Status is DownloadStatus.Completed
                    or DownloadStatus.Failed
                    or DownloadStatus.Cancelled)
                .Select(t => t.Id)
                .ToList();

            foreach (var id in toRemove)
                _tasks.TryRemove(id, out _);
        }

        //  核心下载逻辑

        private async Task<DownloadTask> ExecuteAsync(DownloadTask task, SemaphoreSlim semaphore)
        {
            // 使用传入的 semaphore 引用，而非字段（防止热替换后 Release 错误的信号量）
            await semaphore.WaitAsync(task.Cts.Token);
            try
            {
                task.Status = DownloadStatus.Downloading;
                task.StartTime = DateTime.Now;

                string? dir = Path.GetDirectoryName(task.DestinationPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string tempPath = task.DestinationPath + ".smsmtmp";

                using var response = await _httpClient.GetAsync(
                    task.Url, HttpCompletionOption.ResponseHeadersRead, task.Cts.Token);

                response.EnsureSuccessStatusCode();

                task.TotalBytes = response.Content.Headers.ContentLength ?? -1;

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(task.Cts.Token);

                {
                    await using var fileStream = new FileStream(
                        tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        BufferSize, useAsync: true);

                    var buffer = new byte[BufferSize];
                    var sw = Stopwatch.StartNew();
                    long lastReportedBytes = 0;
                    long lastReportTime = 0;
                    long speed = 0;

                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(
                        buffer.AsMemory(0, BufferSize), task.Cts.Token)) > 0)
                    {
                        await fileStream.WriteAsync(
                            buffer.AsMemory(0, bytesRead), task.Cts.Token);
                        task.BytesDownloaded += bytesRead;

                        long elapsed = sw.ElapsedMilliseconds;
                        if (elapsed - lastReportTime >= 200)
                        {
                            long deltaBytes = task.BytesDownloaded - lastReportedBytes;
                            double deltaSec = (elapsed - lastReportTime) / 1000.0;
                            speed = deltaSec > 0 ? (long)(deltaBytes / deltaSec) : 0;

                            lastReportedBytes = task.BytesDownloaded;
                            lastReportTime = elapsed;

                            RaiseProgress(task, speed);
                        }
                    }

                    await fileStream.FlushAsync(task.Cts.Token);
                }

                if (!string.IsNullOrEmpty(task.ExpectedHash))
                {
                    string actualHash = await ComputeHashAsync(
                        tempPath, task.HashAlgorithm, task.Cts.Token);

                    if (!actualHash.Equals(task.ExpectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(tempPath);
                        throw new InvalidDataException(
                            $"Hash mismatch: expected {task.ExpectedHash}, got {actualHash}");
                    }
                }

                if (File.Exists(task.DestinationPath))
                    File.Delete(task.DestinationPath);
                File.Move(tempPath, task.DestinationPath);

                task.Status = DownloadStatus.Completed;
                task.EndTime = DateTime.Now;

                RaiseProgress(task, 0, isCompleted: true);
                TaskCompleted?.Invoke(this, task);

                return task;
            }
            catch (OperationCanceledException)
            {
                task.Status = DownloadStatus.Cancelled;
                task.ErrorMessage = "Download was cancelled.";
                task.EndTime = DateTime.Now;
                CleanupTemp(task);
                TaskFailed?.Invoke(this, task);
                return task;
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Failed;
                task.ErrorMessage = ex.Message;
                task.EndTime = DateTime.Now;
                CleanupTemp(task);
                RaiseProgress(task, 0, isFailed: true, errorMessage: ex.Message);
                TaskFailed?.Invoke(this, task);
                return task;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private void RaiseProgress(DownloadTask task, long speed,
            bool isCompleted = false, bool isFailed = false, string? errorMessage = null)
        {
            ProgressChanged?.Invoke(this, new DownloadProgressInfo
            {
                TaskId = task.Id,
                DisplayName = task.DisplayName,
                BytesDownloaded = task.BytesDownloaded,
                TotalBytes = task.TotalBytes,
                SpeedBytesPerSecond = speed,
                IsCompleted = isCompleted,
                IsFailed = isFailed,
                ErrorMessage = errorMessage ?? task.ErrorMessage
            });
        }

        private static void CleanupTemp(DownloadTask task)
        {
            try
            {
                string tmp = task.DestinationPath + ".smsmtmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { /* best effort */ }
        }

        private static async Task<string> ComputeHashAsync(
            string filePath, string algorithm, CancellationToken ct)
        {
            using var ha = algorithm.ToUpperInvariant() switch
            {
                "SHA1" => (HashAlgorithm)SHA1.Create(),
                "SHA256" => SHA256.Create(),
                "SHA512" => SHA512.Create(),
                "MD5" => MD5.Create(),
                _ => SHA256.Create()
            };

            await using var fs = new FileStream(
                filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, BufferSize, useAsync: true);

            var hash = await ha.ComputeHashAsync(fs, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public void Dispose()
        {
            CancelAll();
            _semaphore.Dispose();
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }
    }
}