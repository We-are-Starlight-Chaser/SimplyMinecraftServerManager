// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces
{
    /// <summary>
    /// 定义自定义工具扩展的契约，用于在工具页面中显示自定义按钮。
    /// </summary>
    public interface ICustomTool : IExtension
    {
        /// <summary>
        /// 获取工具按钮的配置信息，使用 Wpf.Ui.Controls.Button 以保证工具页面统一性。
        /// </summary>
        ButtonInfo ControlContent { get;}
    }
}
