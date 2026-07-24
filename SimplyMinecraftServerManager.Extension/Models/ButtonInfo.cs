// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Extension.Models
{
    /// <summary>
    /// 定义工具按钮的信息，包括显示内容、图标、外观样式和点击回调。
    /// </summary>
    /// <param name="Content">按钮显示的文本内容</param>
    /// <param name="Icon">按钮的图标</param>
    /// <param name="Appearance">按钮的外观样式</param>
    /// <param name="OnClick">按钮点击时执行的回调方法</param>
    public record ButtonInfo(
        string Content,
        SymbolRegular Icon,
        ControlAppearance Appearance,
        Func<CancellationToken, Task> OnClick
    )
    {
        /// <summary>
        /// 使用默认值初始化 ButtonInfo 的新实例。
        /// </summary>
        public ButtonInfo() : this("", SymbolRegular.Empty, ControlAppearance.Secondary, (ct) => { return Task.CompletedTask; }) { }
    }
}