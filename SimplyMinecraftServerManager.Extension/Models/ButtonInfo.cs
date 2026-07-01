// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Extension.Models
{
    /// <summary>
    /// 定义工具按钮的信息，包括显示内容、图标、外观样式和点击回调。
    /// </summary>
    public record ButtonInfo(
        /// <summary>按钮显示的文本内容</summary>
        string Content,
        /// <summary>按钮的图标</summary>
        SymbolRegular Icon,
        /// <summary>按钮的外观样式</summary>
        ControlAppearance Appearance,
        /// <summary>按钮点击时执行的回调方法</summary>
        Func<CancellationToken, Task> OnClick
    )
    {
        /// <summary>
        /// 使用默认值初始化 ButtonInfo 的新实例。
        /// </summary>
        public ButtonInfo() : this("", SymbolRegular.Empty, ControlAppearance.Secondary, (ct) => { return Task.CompletedTask; }) { }
    }
}