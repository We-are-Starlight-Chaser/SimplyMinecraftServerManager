// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Models
{
    /// <summary>
    /// 应用通知项模型，表示一条通知的详细信息。
    /// </summary>
    public sealed class AppNotificationItem
    {
        /// <summary>
        /// 获取通知项的唯一标识符。
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>
        /// 获取或设置通知标题。
        /// </summary>
        public string Title { get; init; } = "";

        /// <summary>
        /// 获取或设置通知内容。
        /// </summary>
        public string Content { get; init; } = "";

        /// <summary>
        /// 获取或设置通知的外观样式（如信息、成功、危险等）。
        /// </summary>
        public ControlAppearance Appearance { get; init; } = ControlAppearance.Info;

        /// <summary>
        /// 获取或设置通知显示的持续时间。
        /// </summary>
        public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(5);
    }
}
