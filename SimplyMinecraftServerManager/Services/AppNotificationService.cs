// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Models;
using System.Collections.ObjectModel;

namespace SimplyMinecraftServerManager.Services
{
    /// <summary>
    /// 应用通知服务，用于管理应用内通知的显示和移除。
    /// </summary>
    public sealed class AppNotificationService
    {
        /// <summary>
        /// 获取通知项的可观察集合，用于绑定到UI。
        /// </summary>
        public ObservableCollection<AppNotificationItem> Notifications { get; } = [];

        /// <summary>
        /// 显示一条通知消息。
        /// </summary>
        /// <param name="message">要显示的通知消息。</param>
        /// <param name="duration">通知显示持续时间，如果为 null 则默认为5秒。</param>
        public void Show(TaskNotificationMessage message, TimeSpan? duration = null)
        {
            ArgumentNullException.ThrowIfNull(message);

            Enqueue(new AppNotificationItem
            {
                Title = message.Title,
                Content = message.Content,
                Appearance = message.Appearance,
                Duration = duration ?? TimeSpan.FromSeconds(5)
            });
        }

        /// <summary>
        /// 显示信息类型的通知。
        /// </summary>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知内容。</param>
        /// <param name="duration">通知显示持续时间，如果为 null 则默认为5秒。</param>
        public void ShowInfo(string title, string content, TimeSpan? duration = null) =>
            Show(TaskNotificationMessage.Info(title, content), duration);

        /// <summary>
        /// 显示成功类型的通知。
        /// </summary>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知内容。</param>
        /// <param name="duration">通知显示持续时间，如果为 null 则默认为5秒。</param>
        public void ShowSuccess(string title, string content, TimeSpan? duration = null) =>
            Show(TaskNotificationMessage.Success(title, content), duration);

        /// <summary>
        /// 显示危险类型的通知。
        /// </summary>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知内容。</param>
        /// <param name="duration">通知显示持续时间，如果为 null 则默认为5秒。</param>
        public void ShowDanger(string title, string content, TimeSpan? duration = null) =>
            Show(TaskNotificationMessage.Danger(title, content), duration);

        /// <summary>
        /// 根据ID移除指定的通知。
        /// </summary>
        /// <param name="notificationId">要移除的通知ID。</param>
        public void Remove(Guid notificationId)
        {
            ExecuteOnUiThread(() =>
            {
                var existing = Notifications.FirstOrDefault(item => item.Id == notificationId);
                if (existing != null)
                {
                    Notifications.Remove(existing);
                }
            });
        }

        private void Enqueue(AppNotificationItem item)
        {
            ExecuteOnUiThread(() => Notifications.Insert(0, item));
        }

        private static void ExecuteOnUiThread(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }
    }
}
