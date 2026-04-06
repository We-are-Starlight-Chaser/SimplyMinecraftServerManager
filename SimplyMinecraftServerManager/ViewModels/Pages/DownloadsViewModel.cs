using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.Internals.Downloads;
using SimplyMinecraftServerManager.Models;
using SimplyMinecraftServerManager.Services;
using System.Collections.ObjectModel;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class DownloadsViewModel : ObservableObject, INavigationAware
    {
        [ObservableProperty]
        private ObservableCollection<DownloadTaskItem> _downloadTasks = [];

        [ObservableProperty]
        private int _activeCount = 0;

        [ObservableProperty]
        private int _completedCount = 0;

        /// <summary>
        /// 任务数变化时触发
        /// </summary>
        public event EventHandler<int>? TaskCountChanged;

        private readonly AppNotificationService _notificationService;

        public DownloadsViewModel(AppNotificationService notificationService)
        {
            _notificationService = notificationService;

            // 订阅下载管理器事件
            DownloadManager.Default.TaskQueued -= OnDownloadTaskQueued;
            DownloadManager.Default.TaskQueued += OnDownloadTaskQueued;
            DownloadManager.Default.ProgressChanged -= OnDownloadProgress;
            DownloadManager.Default.ProgressChanged += OnDownloadProgress;
            DownloadManager.Default.TaskCompleted -= OnDownloadTaskCompleted;
            DownloadManager.Default.TaskCompleted += OnDownloadTaskCompleted;
            DownloadManager.Default.TaskFailed -= OnDownloadTaskFailed;
            DownloadManager.Default.TaskFailed += OnDownloadTaskFailed;
            DownloadManager.Default.TaskInstalled -= OnDownloadTaskInstalled;
            DownloadManager.Default.TaskInstalled += OnDownloadTaskInstalled;
        }

        public async Task OnNavigatedToAsync()
        {
            RefreshDownloadTasks();
            await Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void RefreshDownloadTasks()
        {
            DownloadTasks.Clear();
            foreach (var task in DownloadManager.Default.AllTasks)
            {
                var item = new DownloadTaskItem(task);
                item.RequestRemove += OnTaskRequestRemove;
                DownloadTasks.Add(item);
            }
            UpdateCounts();
        }

        private void OnDownloadTaskQueued(object? sender, DownloadTask task)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = DownloadTasks.FirstOrDefault(t => t.Id == task.Id);
                if (item == null)
                {
                    var newItem = new DownloadTaskItem(task);
                    newItem.RequestRemove += OnTaskRequestRemove;
                    DownloadTasks.Insert(0, newItem);
                }

                if (task.NotifyOnCreated)
                {
                    ShowTaskNotification(task.CreatedNotification ?? TaskNotificationMessage.Info(
                        "任务已创建",
                        $"开始下载 {task.DisplayName}..."));
                }

                UpdateCounts();
            });
        }

        private void OnDownloadProgress(object? sender, DownloadProgressInfo e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = DownloadTasks.FirstOrDefault(t => t.TaskId == e.TaskId);
                if (item != null)
                {
                    item.UpdateProgress(e);
                }
                else
                {
                    var newItem = new DownloadTaskItem(e);
                    newItem.RequestRemove += OnTaskRequestRemove;
                    DownloadTasks.Insert(0, newItem);
                }
                UpdateCounts();
            });
        }

        private void OnDownloadTaskCompleted(object? sender, DownloadTask task)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = DownloadTasks.FirstOrDefault(t => t.Id == task.Id);
                if (item != null)
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

                UpdateCounts();
            });
        }

        private void OnDownloadTaskFailed(object? sender, DownloadTask task)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = DownloadTasks.FirstOrDefault(t => t.Id == task.Id);
                if (item != null)
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

                UpdateCounts();
            });
        }

        private void OnDownloadTaskInstalled(object? sender, DownloadTask task)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = DownloadTasks.FirstOrDefault(t => t.Id == task.Id);
                if (item != null)
                {
                    item.UpdateFromTask(task);
                }

                if (task.NotifyOnCompleted)
                {
                    var fallbackMessage = BuildInstalledNotification(task);
                    ShowTaskNotification(task.CompletedNotification ?? fallbackMessage);
                }

                UpdateCounts();
            });
        }

        private void OnTaskRequestRemove(object? sender, EventArgs e)
        {
            if (sender is DownloadTaskItem item)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    item.CancelAutoRemove();
                    DownloadTasks.Remove(item);
                    UpdateCounts();
                });
            }
        }

        private void UpdateCounts()
        {
            // ActiveCount: 进行中 + 暂停 + 等待中（所有未完成的任务）
            ActiveCount = DownloadTasks.Count(t => t.IsActive || t.IsPaused || (!t.IsCompleted && !t.IsFailed && !t.IsActive && !t.IsPaused));
            CompletedCount = DownloadTasks.Count(t => t.IsCompleted || t.IsFailed);
            TaskCountChanged?.Invoke(this, ActiveCount);
        }

        private void ShowTaskNotification(TaskNotificationMessage message)
        {
            _notificationService.Show(message);
        }

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

        [RelayCommand]
        private void CancelTask(DownloadTaskItem? item)
        {
            if (item == null) return;
            DownloadManager.Default.Cancel(item.TaskId);
            item.Status = "已取消";
            item.IsFailed = true;
            UpdateCounts();
        }

        [RelayCommand]
        private void CancelAllTasks()
        {
            foreach (var item in DownloadTasks.Where(t => t.IsActive).ToList())
            {
                DownloadManager.Default.Cancel(item.TaskId);
                item.Status = "已取消";
                item.IsFailed = true;
            }
            UpdateCounts();
        }

        [RelayCommand]
        private async Task PauseTaskAsync(DownloadTaskItem? item)
        {
            if (item == null || !item.IsActive) return;
            DownloadManager.Default.Pause(item.TaskId);
            item.IsPaused = true;
            item.IsActive = false;
            item.Status = "已暂停";
            UpdateCounts();
        }

        [RelayCommand]
        private async Task ResumeTaskAsync(DownloadTaskItem? item)
        {
            if (item == null || !item.IsPaused) return;
            await DownloadManager.Default.ResumeAsync(item.TaskId);
            item.IsPaused = false;
            item.IsActive = true;
            item.Status = "下载中";
            UpdateCounts();
        }

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
            UpdateCounts();
        }

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
            UpdateCounts();
        }

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
                item.CancelAutoRemove();
                DownloadTasks.Remove(item);
                UpdateCounts();
            }
        }

        [RelayCommand]
        private void ClearCompleted()
        {
            // 先从 DownloadManager 中移除已完成的任务
            DownloadManager.Default.ClearFinished();
            
            // 再从 UI 列表中移除
            foreach (var item in DownloadTasks.Where(t => t.IsCompleted || t.IsFailed).ToList())
            {
                item.CancelAutoRemove();
                DownloadTasks.Remove(item);
            }
            UpdateCounts();
        }
    }

    public partial class DownloadTaskItem : ObservableObject
    {
        public string Id { get; }
        public string TaskId { get; }
        public string DisplayName { get; }

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _status = "";

        [ObservableProperty]
        private string _speed = "";

        [ObservableProperty]
        private string _sizeInfo = "";

        [ObservableProperty]
        private bool _isCompleted;

        [ObservableProperty]
        private bool _isFailed;

        [ObservableProperty]
        private bool _isActive;

        [ObservableProperty]
        private bool _isPaused;

        [ObservableProperty]
        private bool _isInstalled;

        private System.Timers.Timer? _autoRemoveTimer;

        public event EventHandler? RequestRemove;

        public DownloadTaskItem(DownloadTask task)
        {
            Id = task.Id;
            TaskId = task.Id;
            DisplayName = task.DisplayName;
            UpdateFromTask(task);
        }

        public DownloadTaskItem(DownloadProgressInfo info)
        {
            TaskId = info.TaskId;
            DisplayName = info.DisplayName;
            Id = TaskId;
            UpdateProgress(info);
        }

        public void UpdateFromTask(DownloadTask task)
        {
            if (task.TotalBytes > 0)
            {
                Progress = (double)task.BytesDownloaded / task.TotalBytes * 100;
                SizeInfo = $"{FormatSize(task.BytesDownloaded)} / {FormatSize(task.TotalBytes)}";
            }
            else
            {
                Progress = 0;
                SizeInfo = FormatSize(task.BytesDownloaded);
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

        public void UpdateProgress(DownloadProgressInfo info)
        {
            // 处理进度显示
            if (info.TotalBytes > 0)
            {
                Progress = info.ProgressPercent >= 0 ? info.ProgressPercent : 0;
                SizeInfo = $"{FormatSize(info.BytesDownloaded)} / {FormatSize(info.TotalBytes)}";
            }
            else
            {
                Progress = 0;
                SizeInfo = FormatSize(info.BytesDownloaded);
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
                Speed = info.SpeedBytesPerSecond > 0
                    ? $"{FormatSize(info.SpeedBytesPerSecond)}/s"
                    : "";
            }
        }

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

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        }

        private void StartAutoRemoveTimer()
        {
            if (_autoRemoveTimer != null) return;

            _autoRemoveTimer = new System.Timers.Timer(30000); // 30 秒
            _autoRemoveTimer.Elapsed += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RequestRemove?.Invoke(this, EventArgs.Empty);
                });
            };
            _autoRemoveTimer.AutoReset = false;
            _autoRemoveTimer.Start();
        }

        public void CancelAutoRemove()
        {
            _autoRemoveTimer?.Stop();
            _autoRemoveTimer?.Dispose();
            _autoRemoveTimer = null;
        }
    }
}
