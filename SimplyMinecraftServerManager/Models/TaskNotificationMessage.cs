// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Models
{
    /// <summary>
    /// 任务通知消息，用于在用户界面中显示任务执行结果的通知。
    /// </summary>
    public sealed class TaskNotificationMessage
    {
        /// <summary>
        /// 通知的标题。
        /// </summary>
        public string Title { get; init; } = "";

        /// <summary>
        /// 通知的内容。
        /// </summary>
        public string Content { get; init; } = "";

        /// <summary>
        /// 通知的外观样式，用于区分不同级别的通知。
        /// </summary>
        public ControlAppearance Appearance { get; init; } = ControlAppearance.Info;

        /// <summary>
        /// 创建一条信息级别的通知消息。
        /// </summary>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知内容。</param>
        /// <returns>信息级别的任务通知消息。</returns>
        public static TaskNotificationMessage Info(string title, string content) => new()
        {
            Title = title,
            Content = content,
            Appearance = ControlAppearance.Info
        };

        /// <summary>
        /// 创建一条成功级别的通知消息。
        /// </summary>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知内容。</param>
        /// <returns>成功级别的任务通知消息。</returns>
        public static TaskNotificationMessage Success(string title, string content) => new()
        {
            Title = title,
            Content = content,
            Appearance = ControlAppearance.Success
        };

        /// <summary>
        /// 创建一条危险（错误）级别的通知消息。
        /// </summary>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知内容。</param>
        /// <returns>危险级别的任务通知消息。</returns>
        public static TaskNotificationMessage Danger(string title, string content) => new()
        {
            Title = title,
            Content = content,
            Appearance = ControlAppearance.Danger
        };
    }
}
