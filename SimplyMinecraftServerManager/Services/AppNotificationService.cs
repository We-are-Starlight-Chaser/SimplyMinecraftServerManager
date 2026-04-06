using SimplyMinecraftServerManager.Models;
using System.Collections.ObjectModel;

namespace SimplyMinecraftServerManager.Services
{
    public sealed class AppNotificationService
    {
        public ObservableCollection<AppNotificationItem> Notifications { get; } = [];

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

        public void ShowInfo(string title, string content, TimeSpan? duration = null) =>
            Show(TaskNotificationMessage.Info(title, content), duration);

        public void ShowSuccess(string title, string content, TimeSpan? duration = null) =>
            Show(TaskNotificationMessage.Success(title, content), duration);

        public void ShowDanger(string title, string content, TimeSpan? duration = null) =>
            Show(TaskNotificationMessage.Danger(title, content), duration);

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
