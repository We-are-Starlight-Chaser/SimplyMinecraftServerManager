using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces
{
    public interface ICustomTool : IExtension
    {
        /// <summary> 
        /// 使用Wpf.Ui.Controls.Button以保证工具页面统一性
        /// </summary>
        ButtonInfo ControlContent { get;}
    }
}
