// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using SimplyMinecraftServerManager.Models;
using SimplyMinecraftServerManager.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    /// <summary>
    /// 下载任务管理页面的视图模型，负责展示、控制和通知所有下载任务。
    /// </summary>
    public partial class DownloadsViewModel : ObservableObject, INavigationAware, IDisposable
    {
        /// <summary>下载任务列表，用于界面绑定。</summary>
        [ObservableProperty]
        private ObservableCollection<DownloadTaskItem> _downloadTasks = [];

        /// <summary>当前活跃（未完成）的任务数量。</summary>
        [ObservableProperty]
        private int _activeCount = 0;

        /// <summary>已完成的任务数量。</summary>
        [ObservableProperty]
        private int _completedCount = 0;

        /// <summary>
        /// 任务数变化时触发
        /// </summary>
        public event EventHandler<int>? TaskCountChanged;

        /// <summary>任务ID到UI项的映射，用于O(1)查找。</summary>
        private readonly Dictionary<string, DownloadTaskItem> _taskItemMap = new();

        /// <summary>应用通知服务，用于显示下载任务的提示消息。</summary>
        private readonly AppNotificationService _notificationService;

        private bool _disposed;
        private bool _countsDirty;
        private DispatcherTimer? _countsRefreshTimer;

        /// <summary>
        /// 初始化下载任务视图模型并订阅下载管理器事件。
        /// </summary>
        /// <param name="notificationService">通知服务。</param>
        public DownloadsViewModel(AppNotificationService notificationService)
        {
            _notificationService = notificationService;

            // 订阅下载管理器事件
            DownloadManager.Default.TaskQueued += OnDownloadTaskQueued;
            DownloadManager.Default.ProgressChanged += OnDownloadProgress;
            DownloadManager.Default.TaskCompleted += OnDownloadTaskCompleted;
            DownloadManager.Default.TaskFailed += OnDownloadTaskFailed;
            DownloadManager.Default.TaskInstalled += OnDownloadTaskInstalled;
        }

        /// <summary>
        /// 导航到此页面时刷新下载任务列表。
        /// </summary>
        public async Task OnNavigatedToAsync()
        {
            RefreshDownloadTasks();
            await Task.CompletedTask;
        }

        /// <summary>
        /// 离开此页面时执行的清理操作。
        /// </summary>
        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        /// <summary>
        /// 从下载管理器重新加载所有任务到界面列表。
        /// </summary>
        private void RefreshDownloadTasks()
        {
            DownloadTasks.Clear();
            _taskItemMap.Clear();
            foreach (var task in DownloadManager.Default.AllTasks)
            {
                var item = new DownloadTaskItem(task);
                item.RequestRemove += OnTaskRequestRemove;
                DownloadTasks.Add(item);
                _taskItemMap[task.Id] = item;
            }
            RefreshCounts();
        }

        /// <summary>
        /// 下载任务入队时的回调，将新任务添加到列表顶部。
        /// </summary>
        private void OnDownloadTaskQueued(object? sender, DownloadTask task)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!_taskItemMap.TryGetValue(task.Id, out var item))
                {
                    var newItem = new DownloadTaskItem(task);
                    newItem.RequestRemove += OnTaskRequestRemove;
                    DownloadTasks.Insert(0, newItem);
                    _taskItemMap[task.Id] = newItem;
                    item = newItem;
                }

                if (task.NotifyOnCreated)
                {
                    ShowTaskNotification(task.CreatedNotification ?? TaskNotificationMessage.Info(
                        "任务已创建",
                        $"开始下载 {task.DisplayName}..."));
                }

                MarkCountsDirty();
            });
        }

        /// <summary>
        /// 下载进度变化时更新对应任务项的进度信息。
        /// </summary>
        private void OnDownloadProgress(object? sender, DownloadProgressInfo e)
        {
            var app = Application.Current;
            if (app == null) return;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (Application.Current == null) return;
                if (_taskItemMap.TryGetValue(e.TaskId, out var item))
                {
                    item.UpdateProgress(e);
                }
                else
                {
                    var newItem = new DownloadTaskItem(e);
                    newItem.RequestRemove += OnTaskRequestRemove;
                    DownloadTasks.Insert(0, newItem);
                    _taskItemMap[e.TaskId] = newItem;
                }
                MarkCountsDirty();
            });
        }

        /// <summary>
        /// 下载任务完成时的回调，更新任务状态并发送通知。
        /// </summary>
        private void OnDownloadTaskCompleted(object? sender, DownloadTask task)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_taskItemMap.TryGetValue(task.Id, out var item))
                {
                    item.UpdateFromTask(task);
                }

                if (task.InstallationStatus != InstallationStatus.Installed)
                {
                    if (task.NotifyOnCompleted)
                    {
                        ShowTaskNotification(task.CompletedNotification ?? TaskNotificationMessage.Success(
                            "任务已完成",
                            $"{task.DisplayName} 已完成。"));
                    }
                }

                MarkCountsDirty();
            });
        }

        /// <summary>
        /// 下载任务失败时的回调，更新任务状态并发送失败通知。
        /// </summary>
        private void OnDownloadTaskFailed(object? sender, DownloadTask task)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_taskItemMap.TryGetValue(task.Id, out var item))
                {
                    item.UpdateFromTask(task);
                }

                if (task.Status != DownloadStatus.Cancelled)
                {
                    if (task.NotifyOnFailed)
                    {
                        ShowTaskNotification(task.FailedNotification ?? TaskNotificationMessage.Danger(
                            "任务失败",
                            string.IsNullOrWhiteSpace(task.ErrorMessage)
                                ? $"{task.DisplayName} 执行失败。"
                                : $"{task.DisplayName}: {task.ErrorMessage}"));
                    }
                }

                MarkCountsDirty();
            });
        }

        /// <summary>
        /// 下载任务安装完成时的回调，更新任务状态并发送安装完成通知。
        /// </summary>
        private void OnDownloadTaskInstalled(object? sender, DownloadTask task)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_taskItemMap.TryGetValue(task.Id, out var item))
                {
                    item.UpdateFromTask(task);
                }

                if (task.NotifyOnCompleted)
                {
                    var fallbackMessage = BuildInstalledNotification(task);
                    ShowTaskNotification(task.CompletedNotification ?? fallbackMessage);
                }

                MarkCountsDirty();
            });
        }

        /// <summary>
        /// 任务项请求移除时的回调，从列表中删除该任务。
        /// </summary>
        private void OnTaskRequestRemove(object? sender, EventArgs e)
        {
            if (sender is DownloadTaskItem item)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    item.Dispose();
                    DownloadTasks.Remove(item);
                    _taskItemMap.Remove(item.TaskId);
                    RefreshCounts();
                });
            }
        }

        /// <summary>
        /// 更新活跃和已完成任务计数，并触发任务数变化事件。
        /// </summary>
        private void MarkCountsDirty()
        {
            if (_countsDirty) return;
            _countsDirty = true;
            _countsRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _countsRefreshTimer.Tick += (_, _) =>
            {
                _countsRefreshTimer.Stop();
                _countsRefreshTimer = null;
                _countsDirty = false;
                RefreshCounts();
            };
            _countsRefreshTimer.Start();
        }

        /// <summary>
        /// 立即刷新活跃和已完成任务计数。
        /// </summary>
        private void RefreshCounts()
        {
            int active = 0;
            int completed = 0;
            foreach (var t in DownloadTasks)
            {
                if (t.IsCompleted || t.IsFailed)
                    completed++;
                else if (t.IsActive || t.IsPaused)
                    active++;
            }
            ActiveCount = active;
            CompletedCount = completed;
            TaskCountChanged?.Invoke(this, ActiveCount);
        }

        /// <summary>
        /// 显示任务通知消息。
        /// </summary>
        private void ShowTaskNotification(TaskNotificationMessage message)
        {
            _notificationService.Show(message);
        }

        /// <summary>
        /// 构建任务安装完成的通知消息。
        /// </summary>
        private static TaskNotificationMessage BuildInstalledNotification(DownloadTask task)
        {
            var targetInstanceName = !string.IsNullOrWhiteSpace(task.TargetInstanceId)
                ? InstanceManager.GetById(task.TargetInstanceId)?.Name
                : null;

            var content = string.IsNullOrWhiteSpace(targetInstanceName)
                ? $"{task.DisplayName} 已安装完成。"
                : $"已将 {task.DisplayName} 安装至实例 {targetInstanceName}。";

            return TaskNotificationMessage.Success("任务已完成", content);
        }

        /// <summary>
        /// 取消指定的下载任务。
        /// </summary>
        [RelayCommand]
        private void CancelTask(DownloadTaskItem? item)
        {
            if (item == null) return;
            DownloadManager.Default.Cancel(item.TaskId);
            item.Status = "已取消";
            item.IsFailed = true;
            RefreshCounts();
        }

        /// <summary>
        /// 取消所有活跃的下载任务。
        /// </summary>
        [RelayCommand]
        private void CancelAllTasks()
        {
            foreach (var item in DownloadTasks.Where(t => t.IsActive).ToList())
            {
                DownloadManager.Default.Cancel(item.TaskId);
                item.Status = "已取消";
                item.IsFailed = true;
            }
            RefreshCounts();
        }

        /// <summary>
        /// 暂停指定的下载任务。
        /// </summary>
        [RelayCommand]
        private async Task PauseTaskAsync(DownloadTaskItem? item)
        {
            if (item == null || !item.IsActive) return;
            DownloadManager.Default.Pause(item.TaskId);
            item.IsPaused = true;
            item.IsActive = false;
            item.Status = "已暂停";
            RefreshCounts();
        }

        /// <summary>
        /// 恢复指定的已暂停下载任务。
        /// </summary>
        [RelayCommand]
        private async Task ResumeTaskAsync(DownloadTaskItem? item)
        {
            if (item == null || !item.IsPaused) return;
            await DownloadManager.Default.ResumeAsync(item.TaskId);
            item.IsPaused = false;
            item.IsActive = true;
            item.Status = "下载中";
            RefreshCounts();
        }

        /// <summary>
        /// 暂停所有活跃的下载任务。
        /// </summary>
        [RelayCommand]
        private void PauseAllTasks()
        {
            DownloadManager.Default.PauseAll();
            foreach (var item in DownloadTasks.Where(t => t.IsActive).ToList())
            {
                item.IsPaused = true;
                item.IsActive = false;
                item.Status = "已暂停";
            }
            RefreshCounts();
        }

        /// <summary>
        /// 恢复所有已暂停的下载任务。
        /// </summary>
        [RelayCommand]
        private async Task ResumeAllTasksAsync()
        {
            await DownloadManager.Default.ResumeAllAsync();
            foreach (var item in DownloadTasks.Where(t => t.IsPaused).ToList())
            {
                item.IsPaused = false;
                item.IsActive = true;
                item.Status = "下载中";
            }
            RefreshCounts();
        }

        /// <summary>
        /// 从列表中移除指定的下载任务（仅限非活跃任务）。
        /// </summary>
        [RelayCommand]
        private void RemoveTask(DownloadTaskItem? item)
        {
            if (item == null) return;
            
            // 任务进行中时不允许删除
            if (item.IsActive)
            {
                return;
            }
            
            // 从DownloadManager中移除任务
            bool removed = DownloadManager.Default.RemoveTask(item.TaskId);
            
            // 如果DownloadManager中没有找到任务，或者移除成功，从UI列表中移除
            if (removed || !DownloadManager.Default.AllTasks.Any(t => t.Id == item.TaskId))
            {
                item.Dispose();
                DownloadTasks.Remove(item);
                _taskItemMap.Remove(item.TaskId);
                RefreshCounts();
            }
        }

        /// <summary>
        /// 清除所有已完成和失败的下载任务。
        /// </summary>
        [RelayCommand]
        private void ClearCompleted()
        {
            // 先从 DownloadManager 中移除已完成的任务
            DownloadManager.Default.ClearFinished();
            
            // 再从 UI 列表中移除
            foreach (var item in DownloadTasks.Where(t => t.IsCompleted || t.IsFailed).ToList())
            {
                item.Dispose();
                DownloadTasks.Remove(item);
                _taskItemMap.Remove(item.TaskId);
            }
            RefreshCounts();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _countsRefreshTimer?.Stop();
            _countsRefreshTimer = null;

            DownloadManager.Default.TaskQueued -= OnDownloadTaskQueued;
            DownloadManager.Default.ProgressChanged -= OnDownloadProgress;
            DownloadManager.Default.TaskCompleted -= OnDownloadTaskCompleted;
            DownloadManager.Default.TaskFailed -= OnDownloadTaskFailed;
            DownloadManager.Default.TaskInstalled -= OnDownloadTaskInstalled;

            foreach (var item in DownloadTasks)
            {
                item.RequestRemove -= OnTaskRequestRemove;
                item.Dispose();
            }
            DownloadTasks.Clear();
            _taskItemMap.Clear();

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 下载任务项，封装单个下载任务的界面显示数据和状态。
    /// </summary>
    public partial class DownloadTaskItem : ObservableObject, IDisposable
    {
        /// <summary>任务唯一标识符。</summary>
        public string Id { get; }

        /// <summary>任务标识符（与 Id 相同）。</summary>
        public string TaskId { get; }

        /// <summary>任务的显示名称。</summary>
        public string DisplayName { get; }

        /// <summary>下载进度百分比（0-100）。</summary>
        [ObservableProperty]
        private double _progress;

        /// <summary>任务状态文本。</summary>
        [ObservableProperty]
        private string _status = "";

        /// <summary>当前下载速度文本。</summary>
        [ObservableProperty]
        private string _speed = "";

        /// <summary>已下载/总大小的显示文本。</summary>
        [ObservableProperty]
        private string _sizeInfo = "";

        /// <summary>指示任务是否已完成。</summary>
        [ObservableProperty]
        private bool _isCompleted;

        /// <summary>指示任务是否失败。</summary>
        [ObservableProperty]
        private bool _isFailed;

        /// <summary>指示任务是否正在活跃执行。</summary>
        [ObservableProperty]
        private bool _isActive;

        /// <summary>指示任务是否已暂停。</summary>
        [ObservableProperty]
        private bool _isPaused;

        /// <summary>指示任务是否已安装完成。</summary>
        [ObservableProperty]
        private bool _isInstalled;

        /// <summary>自动移除计时器，任务完成后一段时间自动从列表移除。</summary>
        private DispatcherTimer? _autoRemoveTimer;

        /// <summary>缓存上次格式化的下载大小，避免重复分配字符串。</summary>
        private long _lastFormattedDownloaded;
        private long _lastFormattedTotal;
        private string _lastFormattedSpeed = "";

        /// <summary>请求从列表中移除此任务项的事件。</summary>
        public event EventHandler? RequestRemove;

        /// <summary>
        /// 使用下载任务数据初始化任务项。
        /// </summary>
        /// <param name="task">下载任务。</param>
        public DownloadTaskItem(DownloadTask task)
        {
            Id = task.Id;
            TaskId = task.Id;
            DisplayName = task.DisplayName;
            UpdateFromTask(task);
        }

        /// <summary>
        /// 使用进度信息初始化任务项（用于尚无对应 DownloadTask 的进度回调）。
        /// </summary>
        /// <param name="info">下载进度信息。</param>
        public DownloadTaskItem(DownloadProgressInfo info)
        {
            TaskId = info.TaskId;
            DisplayName = info.DisplayName;
            Id = TaskId;
            UpdateProgress(info);
        }

        /// <summary>
        /// 根据下载任务的当前状态更新界面显示数据。
        /// </summary>
        /// <param name="task">下载任务。</param>
        public void UpdateFromTask(DownloadTask task)
        {
            if (task.TotalBytes > 0)
            {
                Progress = (double)task.BytesDownloaded / task.TotalBytes * 100;
                if (task.BytesDownloaded != _lastFormattedDownloaded || task.TotalBytes != _lastFormattedTotal)
                {
                    SizeInfo = $"{FormatSize(task.BytesDownloaded)} / {FormatSize(task.TotalBytes)}";
                    _lastFormattedDownloaded = task.BytesDownloaded;
                    _lastFormattedTotal = task.TotalBytes;
                }
            }
            else
            {
                Progress = 0;
                if (task.BytesDownloaded != _lastFormattedDownloaded)
                {
                    SizeInfo = FormatSize(task.BytesDownloaded);
                    _lastFormattedDownloaded = task.BytesDownloaded;
                }
            }

            IsCompleted = task.Status == DownloadStatus.Completed;
            IsFailed = task.Status == DownloadStatus.Failed || task.Status == DownloadStatus.Cancelled;
            IsActive = task.Status == DownloadStatus.Downloading;
            IsPaused = task.Status == DownloadStatus.Paused;
            IsInstalled = task.InstallationStatus == InstallationStatus.Installed;
            
            // 更新状态文本
            if (IsInstalled)
            {
                Status = "安装成功";
            }
            else if (task.InstallationStatus == InstallationStatus.InstallationFailed)
            {
                Status = $"安装失败: {task.ErrorMessage}";
                IsFailed = true;
            }
            else
            {
                Status = GetStatusText(task.Status);
            }
            
            Speed = "";

            if (IsCompleted || IsFailed || IsInstalled)
            {
                StartAutoRemoveTimer();
            }
        }

        /// <summary>
        /// 根据下载进度信息更新界面显示数据。
        /// </summary>
        /// <param name="info">下载进度信息。</param>
        public void UpdateProgress(DownloadProgressInfo info)
        {
            // 处理进度显示
            if (info.TotalBytes > 0)
            {
                Progress = info.ProgressPercent >= 0 ? info.ProgressPercent : 0;
                if (info.BytesDownloaded != _lastFormattedDownloaded || info.TotalBytes != _lastFormattedTotal)
                {
                    SizeInfo = $"{FormatSize(info.BytesDownloaded)} / {FormatSize(info.TotalBytes)}";
                    _lastFormattedDownloaded = info.BytesDownloaded;
                    _lastFormattedTotal = info.TotalBytes;
                }
            }
            else
            {
                Progress = 0;
                if (info.BytesDownloaded != _lastFormattedDownloaded)
                {
                    SizeInfo = FormatSize(info.BytesDownloaded);
                    _lastFormattedDownloaded = info.BytesDownloaded;
                }
            }

            IsCompleted = info.IsCompleted;
            IsFailed = info.IsFailed;
            IsPaused = info.IsPaused;
            IsActive = !info.IsCompleted && !info.IsFailed && !info.IsPaused && !info.IsInstalled;
            IsInstalled = info.IsInstalled;

            if (info.IsPaused)
            {
                Status = "已暂停";
                Speed = "";
            }
            else if (info.IsInstalled)
            {
                Status = "安装成功";
                Speed = "";
                Progress = 100;
                StartAutoRemoveTimer();
            }
            else if (info.IsCompleted)
            {
                Status = "已完成";
                Speed = "";
                Progress = 100;
                StartAutoRemoveTimer();
            }
            else if (info.IsFailed)
            {
                Status = $"失败: {info.ErrorMessage}";
                Speed = "";
                StartAutoRemoveTimer();
            }
            else
            {
                Status = "下载中";
                string newSpeed = info.SpeedBytesPerSecond > 0
                    ? $"{FormatSize(info.SpeedBytesPerSecond)}/s"
                    : "";
                if (newSpeed != _lastFormattedSpeed)
                {
                    Speed = newSpeed;
                    _lastFormattedSpeed = newSpeed;
                }
            }
        }

        /// <summary>
        /// 根据下载状态枚举获取对应的中文状态文本。
        /// </summary>
        private static string GetStatusText(DownloadStatus status) => status switch
        {
            DownloadStatus.Pending => "等待中",
            DownloadStatus.Downloading => "下载中",
            DownloadStatus.Paused => "已暂停",
            DownloadStatus.Completed => "已完成",
            DownloadStatus.Failed => "失败",
            DownloadStatus.Cancelled => "已取消",
            _ => "未知"
        };

        /// <summary>
        /// 将字节数格式化为人类可读的大小文本。
        /// </summary>
        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        }

        /// <summary>
        /// 启动自动移除计时器，任务完成后 30 秒自动从列表移除。
        /// </summary>
        private void StartAutoRemoveTimer()
        {
            if (_autoRemoveTimer != null) return;

            _autoRemoveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _autoRemoveTimer.Tick += (_, _) =>
            {
                _autoRemoveTimer?.Stop();
                _autoRemoveTimer = null;
                RequestRemove?.Invoke(this, EventArgs.Empty);
            };
            _autoRemoveTimer.Start();
        }

        /// <summary>
        /// 取消自动移除计时器，阻止任务项被自动从列表删除。
        /// </summary>
        public void CancelAutoRemove()
        {
            _autoRemoveTimer?.Stop();
            _autoRemoveTimer = null;
        }

        /// <summary>
        /// 释放资源，停止自动移除计时器。
        /// </summary>
        public void Dispose()
        {
            CancelAutoRemove();
            GC.SuppressFinalize(this);
        }
    }
}
