// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Models
{
    public sealed class AppNotificationItem
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Title { get; init; } = "";

        public string Content { get; init; } = "";

        public ControlAppearance Appearance { get; init; } = ControlAppearance.Info;

        public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(5);
    }
}
