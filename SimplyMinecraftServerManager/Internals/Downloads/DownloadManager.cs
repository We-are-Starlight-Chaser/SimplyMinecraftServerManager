// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SimplyMinecraftServerManager.Internals;

namespace SimplyMinecraftServerManager.Internals.Downloads
{
    /// <summary>
    /// 下载管理器，负责管理所有下载任务的生命周期。
    /// 支持并发控制、断点续传、暂停/恢复、取消、哈希校验及下载后自动安装。
    /// </summary>
    public sealed class DownloadManager : IDisposable
    {
        private static readonly Lock _defaultLock = new();
        private static DownloadManager? _default;

        /// <summary>
        /// 获取全局单例下载管理器，使用配置中的下载线程数初始化。
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
        /// 使用新的并发数重新创建全局单例下载管理器。
        /// </summary>
        /// <param name="maxConcurrentDownloads">最大并发下载数</param>
        public static void ReconfigureDefault(int maxConcurrentDownloads)
        {
            lock (_defaultLock)
            {
                _default = new DownloadManager(maxConcurrentDownloads);
            }
        }

private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly ConcurrentDictionary<string, DownloadTask> _tasks = new();
        private readonly ConcurrentDictionary<string, bool> _pausedTasks = new();
        private const int BufferSize = 262144;
        
        private const int ProgressReportIntervalMs = 150;

        private volatile SemaphoreSlim _semaphore;
        private volatile int _maxConcurrent;

        /// <summary>下载进度变更事件</summary>
        public event EventHandler<DownloadProgressInfo>? ProgressChanged;
        /// <summary>任务入队事件</summary>
        public event EventHandler<DownloadTask>? TaskQueued;
        /// <summary>任务完成事件</summary>
        public event EventHandler<DownloadTask>? TaskCompleted;
        /// <summary>任务失败事件</summary>
        public event EventHandler<DownloadTask>? TaskFailed;
        /// <summary>任务安装完成事件（仅 DownloadAndInstall 类型任务触发）</summary>
        public event EventHandler<DownloadTask>? TaskInstalled;

        /// <summary>当前最大并发下载数</summary>
        public int MaxConcurrentDownloads => _maxConcurrent;

        private static readonly string[] AllowedHosts =
        [
            "api.papermc.io",
            "papermc.io",
            "download.mojang.com",
            "github.com",
            "githubusercontent.com",
            "modrinth.com",
            "cdn.modrinth.com",
            "leavesmc.org",
            "purpurmc.org",
            "github.io",
            "adoptium.net",
            "azul.com",
            "zulu.org"
        ];

        private static readonly ConcurrentDictionary<string, Func<HashAlgorithm>> HashAlgorithmFactories = new()
        {
            ["SHA1"] = () => SHA1.Create(),
            ["SHA256"] = () => SHA256.Create(),
            ["SHA512"] = () => SHA512.Create(),
            ["MD5"] = () => MD5.Create()
        };

        /// <summary>
        /// 创建下载管理器实例。
        /// </summary>
        /// <param name="maxConcurrentDownloads">最大并发下载数，范围 1-32</param>
        /// <param name="httpClient">可选的共享 HttpClient，为 null 时自动创建</param>
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
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = ValidateCertificate
                };
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

        private static bool ValidateCertificate(
            HttpRequestMessage message,
            X509Certificate2? certificate,
            X509Chain? chain,
            SslPolicyErrors errors)
        {
            if (certificate == null) return false;

            string host = message.RequestUri?.Host ?? "";
            bool isAllowedHost = AllowedHosts.Any(h =>
                host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

            if (!isAllowedHost)
            {
                return false;
            }

            if (errors != SslPolicyErrors.None)
            {
                return false;
            }

            if (chain != null)
            {
                try
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                    chain.Build(certificate);
                }
                catch
                {
                }
            }

            return true;
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

        /// <summary>获取所有下载任务的只读列表</summary>
        public IReadOnlyList<DownloadTask> AllTasks => _tasks.Values.ToList().AsReadOnly();
        /// <summary>当前正在下载的任务数量</summary>
        public int ActiveCount => _tasks.Values.Count(t => t.Status == DownloadStatus.Downloading);

        // 入队

        /// <summary>
        /// 异步入队下载任务并等待其完成。
        /// </summary>
        /// <param name="downloadTask">下载任务</param>
        /// <returns>完成后的下载任务</returns>
        public Task<DownloadTask> EnqueueAsync(DownloadTask downloadTask)
        {
            _tasks[downloadTask.Id] = downloadTask;
            TaskQueued?.Invoke(this, downloadTask);
            // 捕获当前 semaphore 引用，保证任务全生命周期使用同一个
            var semaphore = _semaphore;
            return Task.Run(() => ExecuteAsync(downloadTask, semaphore));
        }

        /// <summary>
        /// 同步入队下载任务，不等待其完成（火后不管模式）。
        /// </summary>
        /// <param name="downloadTask">下载任务</param>
        /// <returns>入队的下载任务</returns>
        public DownloadTask Queue(DownloadTask downloadTask)
        {
            _tasks[downloadTask.Id] = downloadTask;
            TaskQueued?.Invoke(this, downloadTask);
            var semaphore = _semaphore;
            _ = Task.Run(() => ExecuteAsync(downloadTask, semaphore));
            return downloadTask;
        }

        /// <summary>
        /// 异步入队下载任务，根据 URL 和目标路径创建任务。
        /// </summary>
        /// <param name="url">下载 URL</param>
        /// <param name="destinationPath">保存的本地完整路径</param>
        /// <param name="displayName">显示名称，为空时使用文件名</param>
        /// <param name="expectedHash">预期文件哈希值（可选）</param>
        /// <param name="hashAlgorithm">哈希算法，默认 SHA256</param>
        /// <returns>完成后的下载任务</returns>
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

        /// <summary>
        /// 异步入队下载并安装任务，下载完成后自动安装到指定实例。
        /// </summary>
        /// <param name="url">下载 URL</param>
        /// <param name="destinationPath">保存的本地完整路径</param>
        /// <param name="targetInstanceId">目标实例 ID</param>
        /// <param name="displayName">显示名称，为空时使用文件名</param>
        /// <param name="expectedHash">预期文件哈希值（可选）</param>
        /// <param name="hashAlgorithm">哈希算法，默认 SHA256</param>
        /// <returns>完成后的下载任务</returns>
        public Task<DownloadTask> EnqueueDownloadAndInstallAsync(
            string url,
            string destinationPath,
            string targetInstanceId,
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
                HashAlgorithm = hashAlgorithm,
                Type = TaskType.DownloadAndInstall,
                TargetInstanceId = targetInstanceId
            };
            return EnqueueAsync(task);
        }

        /// <summary>
        /// 同步入队下载并安装任务，下载完成后自动安装到指定实例（火后不管模式）。
        /// </summary>
        /// <param name="url">下载 URL</param>
        /// <param name="destinationPath">保存的本地完整路径</param>
        /// <param name="targetInstanceId">目标实例 ID</param>
        /// <param name="displayName">显示名称，为空时使用文件名</param>
        /// <param name="expectedHash">预期文件哈希值（可选）</param>
        /// <param name="hashAlgorithm">哈希算法，默认 SHA256</param>
        /// <returns>入队的下载任务</returns>
        public DownloadTask QueueDownloadAndInstall(
            string url,
            string destinationPath,
            string targetInstanceId,
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
                HashAlgorithm = hashAlgorithm,
                Type = TaskType.DownloadAndInstall,
                TargetInstanceId = targetInstanceId
            };

            return Queue(task);
        }

        /// <summary>
        /// 批量异步入队多个下载任务并等待所有任务完成。
        /// </summary>
        /// <param name="tasks">下载任务集合</param>
        /// <returns>所有完成后的下载任务数组</returns>
        public async Task<DownloadTask[]> EnqueueBatchAsync(IEnumerable<DownloadTask> tasks)
        {
            var running = tasks.Select(t => EnqueueAsync(t)).ToArray();
            return await Task.WhenAll(running);
        }

        // 取消

        /// <summary>
        /// 取消指定的下载任务。
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        public void Cancel(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.Cts.Cancel();
                task.Status = DownloadStatus.Cancelled;
            }
        }

        /// <summary>
        /// 取消所有下载任务。
        /// </summary>
        public void CancelAll()
        {
            foreach (var task in _tasks.Values)
            {
                task.Cts.Cancel();
                if (task.Status is DownloadStatus.Pending or DownloadStatus.Downloading)
                    task.Status = DownloadStatus.Cancelled;
            }
        }

        // 暂停

        /// <summary>
        /// 暂停指定的下载任务，支持断点续传时保存已下载进度。
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        public void Pause(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task) && task.Status == DownloadStatus.Downloading)
            {
                task.PausedPosition = task.BytesDownloaded;
                _pausedTasks[taskId] = true;  // 标记为暂停
                task.Status = DownloadStatus.Paused;
                RaiseProgress(task, 0, isPaused: true, installationStatus: task.InstallationStatus);
            }
        }

        /// <summary>
        /// 暂停所有正在下载的任务。
        /// </summary>
        public void PauseAll()
        {
            foreach (var task in _tasks.Values)
            {
                if (task.Status == DownloadStatus.Downloading)
                {
                    task.PausedPosition = task.BytesDownloaded;
                    _pausedTasks[task.Id] = true;  // 标记为暂停
                    task.Status = DownloadStatus.Paused;
                    RaiseProgress(task, 0, isPaused: true, installationStatus: task.InstallationStatus);
                }
            }
        }

        // 继续

        /// <summary>
        /// 恢复指定的已暂停下载任务，支持断点续传。
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        public async Task ResumeAsync(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task) && task.Status == DownloadStatus.Paused)
            {
                _pausedTasks.TryRemove(taskId, out _);  // 移除暂停标记
                
                // 创建新的 CancellationTokenSource 以便可以再次取消
                task.Cts.Dispose();
                task.Cts = new CancellationTokenSource();
                
                var semaphore = _semaphore;
                _ = Task.Run(() => ExecuteAsync(task, semaphore));
            }
        }

        /// <summary>
        /// 恢复所有已暂停的下载任务。
        /// </summary>
        public async Task ResumeAllAsync()
        {
            var pausedTasks = _tasks.Values.Where(t => t.Status == DownloadStatus.Paused).ToList();
            foreach (var task in pausedTasks)
            {
                task.Cts.Dispose();
                task.Cts = new CancellationTokenSource();
                
                var semaphore = _semaphore;
                _ = Task.Run(() => ExecuteAsync(task, semaphore));
            }
        }

        /// <summary>
        /// 清除所有已完成、失败、取消或暂停的任务。
        /// </summary>
        public void ClearFinished()
        {
            var toRemove = _tasks.Values
                .Where(t => t.Status is DownloadStatus.Completed
                    or DownloadStatus.Failed
                    or DownloadStatus.Cancelled
                    or DownloadStatus.Paused)
                .Select(t => t.Id)
                .ToList();

            foreach (var id in toRemove)
                _tasks.TryRemove(id, out _);
        }

        /// <summary>
        /// 移除指定任务
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveTask(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                // 如果任务正在下载，先取消
                if (task.Status == DownloadStatus.Downloading)
                {
                    task.Cts.Cancel();
                    task.Status = DownloadStatus.Cancelled;
                }
                // 如果任务已暂停，也取消
                else if (task.Status == DownloadStatus.Paused)
                {
                    task.Cts.Cancel();
                    task.Status = DownloadStatus.Cancelled;
                }
                
                return _tasks.TryRemove(taskId, out _);
            }
            return false;
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

                // 检查是否支持断点续传
                bool supportsResume = task.PausedPosition > 0 && File.Exists(tempPath);
                var request = new HttpRequestMessage(HttpMethod.Get, task.Url);
                if (supportsResume)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(task.PausedPosition, null);
                }

                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, task.Cts.Token);

                response.EnsureSuccessStatusCode();

                // 检测是否支持断点续传（检查 Accept-Ranges 头）
                var acceptRangesHeader = response.Headers.FirstOrDefault(h => 
                    h.Key.Equals("Accept-Ranges", StringComparison.OrdinalIgnoreCase));
                task.IsResumable = acceptRangesHeader.Value?.FirstOrDefault() == "bytes" 
                    || response.StatusCode == System.Net.HttpStatusCode.PartialContent;

                task.TotalBytes = response.Content.Headers.ContentLength ?? -1;
                if (task.IsResumable && task.PausedPosition > 0 && task.TotalBytes > 0)
                {
                    task.TotalBytes += task.PausedPosition;
                }

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(task.Cts.Token);

                {
// 如果支持断点续传且有暂停位置，则追加写入；否则创建新文件
                    var fileMode = (supportsResume && task.PausedPosition > 0) ? FileMode.Append : FileMode.Create;
                    await using var fileStream = new FileStream(
                        tempPath, fileMode, FileAccess.Write, FileShare.None,
                        BufferSize, useAsync: true);

                    var sw = Stopwatch.StartNew();
                    long lastReportedBytes = task.PausedPosition;
                    long lastReportTime = 0;
                    long speed = 0;

                    byte[] buffer = new byte[BufferSize];
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(
                        buffer.AsMemory(0, BufferSize), task.Cts.Token)) > 0)
                    {
                        // 检查是否被暂停
                        if (_pausedTasks.ContainsKey(task.Id))
                        {
                            // 暂停下载
                            task.Status = DownloadStatus.Paused;
                            _pausedTasks.TryRemove(task.Id, out _);
                            RaiseProgress(task, 0, isPaused: true, installationStatus: task.InstallationStatus);
                            return task;
                        }

                        await fileStream.WriteAsync(
                            buffer.AsMemory(0, bytesRead), task.Cts.Token);
                        task.BytesDownloaded += bytesRead;

                        long elapsed = sw.ElapsedMilliseconds;
                        if (elapsed - lastReportTime >= ProgressReportIntervalMs)
                        {
                            long deltaBytes = task.BytesDownloaded - lastReportedBytes;
                            double deltaSec = (elapsed - lastReportTime) / 1000.0;
                            speed = deltaSec > 0 ? (long)(deltaBytes / deltaSec) : 0;

                            lastReportedBytes = task.BytesDownloaded;
                            lastReportTime = elapsed;

                            RaiseProgress(task, speed, installationStatus: task.InstallationStatus);
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

                // 如果任务是下载并安装类型，执行安装
                if (task.Type == TaskType.DownloadAndInstall && !string.IsNullOrEmpty(task.TargetInstanceId))
                {
                    try
                    {
                        task.InstallationStatus = InstallationStatus.Installing;
                        task.InstallationStartTime = DateTime.Now;
                        
                        // 更新进度显示安装中状态
                        RaiseProgress(task, 0, isCompleted: false, installationStatus: task.InstallationStatus);
                        
                        // 执行安装
                        var pluginInfo = PluginManager.InstallPlugin(task.TargetInstanceId, task.DestinationPath);
                        
                        task.InstallationStatus = InstallationStatus.Installed;
                        task.InstallationEndTime = DateTime.Now;
                        
                        // 触发安装完成事件
                        TaskInstalled?.Invoke(this, task);
                    }
                    catch (Exception ex)
                    {
                        task.InstallationStatus = InstallationStatus.InstallationFailed;
                        task.InstallationEndTime = DateTime.Now;
                        task.ErrorMessage = $"安装失败: {ex.Message}";
                        
                        // 触发任务失败事件
                        TaskFailed?.Invoke(this, task);
                        RaiseProgress(task, 0, isFailed: true, errorMessage: task.ErrorMessage, installationStatus: task.InstallationStatus);
                        return task;
                    }
                }

                task.Status = DownloadStatus.Completed;
                task.EndTime = DateTime.Now;

                RaiseProgress(task, 0, isCompleted: true, installationStatus: task.InstallationStatus);
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
                RaiseProgress(task, 0, isFailed: true, errorMessage: ex.Message, installationStatus: task.InstallationStatus);
                TaskFailed?.Invoke(this, task);
                return task;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private void RaiseProgress(DownloadTask task, long speed,
            bool isCompleted = false, bool isFailed = false, bool isPaused = false, string? errorMessage = null,
            InstallationStatus installationStatus = InstallationStatus.NotStarted)
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
                IsPaused = isPaused,
                ErrorMessage = errorMessage ?? task.ErrorMessage,
                TaskType = task.Type,
                InstallationStatus = installationStatus
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
            var factory = HashAlgorithmFactories.GetValueOrDefault(
                algorithm.ToUpperInvariant(), () => SHA256.Create());

            using var ha = factory();

            await using var fs = new FileStream(
                filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, BufferSize, useAsync: true);

            var hash = await ha.ComputeHashAsync(fs, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// 释放下载管理器资源，取消所有任务并释放 HttpClient（如果是内部创建的）。
        /// </summary>
        public void Dispose()
        {
            CancelAll();
            _semaphore.Dispose();
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }
    }
}
