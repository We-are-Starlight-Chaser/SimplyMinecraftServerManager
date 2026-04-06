using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Models
{
    public sealed class TaskNotificationMessage
    {
        public string Title { get; init; } = "";

        public string Content { get; init; } = "";

        public ControlAppearance Appearance { get; init; } = ControlAppearance.Info;

        public static TaskNotificationMessage Info(string title, string content) => new()
        {
            Title = title,
            Content = content,
            Appearance = ControlAppearance.Info
        };

        public static TaskNotificationMessage Success(string title, string content) => new()
        {
            Title = title,
            Content = content,
            Appearance = ControlAppearance.Success
        };

        public static TaskNotificationMessage Danger(string title, string content) => new()
        {
            Title = title,
            Content = content,
            Appearance = ControlAppearance.Danger
        };
    }
}
