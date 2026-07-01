// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Extension.Models
{
    public record ButtonInfo(
        string Content,
        SymbolRegular Icon,
        ControlAppearance Appearance,
        Func<CancellationToken, Task> OnClick
    )
    {
        public ButtonInfo() : this("", SymbolRegular.Empty, ControlAppearance.Secondary, (ct) => { return Task.CompletedTask; }) { }
    }
}