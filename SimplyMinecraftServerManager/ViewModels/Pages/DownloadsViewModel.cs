using SimplyMinecraftServerManager.Internals.Downloads;
using System.Collections.ObjectModel;
using Wpf.Ui.Abstractions.Controls;

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

        public DownloadsViewModel()
        {
            // 订阅下载管理器事件
            DownloadManager.Default.ProgressChanged -= OnDownloadProgress;
            DownloadManager.Default.ProgressChanged += OnDownloadProgress;
            DownloadManager.Default.TaskCompleted -= OnDownloadTaskCompleted;
            DownloadManager.Default.TaskFailed -= OnDownloadTaskFailed;
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
            ActiveCount = DownloadTasks.Count(t => t.IsActive);
            CompletedCount = DownloadTasks.Count(t => t.IsCompleted || t.IsFailed);
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
        private void RemoveTask(DownloadTaskItem? item)
        {
            if (item == null) return;
            item.CancelAutoRemove();
            DownloadTasks.Remove(item);
            UpdateCounts();
        }

        [RelayCommand]
        private void ClearCompleted()
        {
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
            Status = GetStatusText(task.Status);
            Speed = "";

            if (IsCompleted || IsFailed)
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
            IsActive = !info.IsCompleted && !info.IsFailed;

            if (info.IsCompleted)
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
