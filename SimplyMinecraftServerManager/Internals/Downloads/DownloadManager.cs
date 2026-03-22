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
    public sealed class DownloadManager : IDisposable
    {
        private static readonly object _defaultLock = new();
        private static DownloadManager? _default;

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
        private const int BufferSize = 81920;

        private volatile SemaphoreSlim _semaphore;
        private volatile int _maxConcurrent;

        public event EventHandler<DownloadProgressInfo>? ProgressChanged;
        public event EventHandler<DownloadTask>? TaskCompleted;
        public event EventHandler<DownloadTask>? TaskFailed;
        public event EventHandler<DownloadTask>? TaskInstalled;

        public int MaxConcurrentDownloads => _maxConcurrent;

        private static readonly string[] AllowedHosts = new[]
        {
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
        };

        private static readonly ConcurrentDictionary<string, Func<HashAlgorithm>> HashAlgorithmFactories = new()
        {
            ["SHA1"] = () => SHA1.Create(),
            ["SHA256"] = () => SHA256.Create(),
            ["SHA512"] = () => SHA512.Create(),
            ["MD5"] = () => MD5.Create()
        };

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

        // 暂停

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

                    var buffer = new byte[BufferSize];
                    var sw = Stopwatch.StartNew();
                    long lastReportedBytes = task.PausedPosition;
                    long lastReportTime = 0;
                    long speed = 0;

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
                        if (elapsed - lastReportTime >= 200)
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

        public void Dispose()
        {
            CancelAll();
            _semaphore.Dispose();
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }
    }
}