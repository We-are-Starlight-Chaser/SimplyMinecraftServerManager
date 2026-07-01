// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using CommunityToolkit.Mvvm.ComponentModel;
using SimplyMinecraftServerManager.Internals;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    /// <summary>
    /// 工具页面的视图模型，提供系统工具功能
    /// </summary>
    public partial class ToolsViewModel : ObservableObject
    {
        /// <summary>
        /// 清理系统内存，释放未使用的内存资源
        /// </summary>
        [RelayCommand]
        private async Task CleanMemoryAsync()
        {
            try
            {
                new MemoryCleaner().CleanMemory();
                await new Wpf.Ui.Controls.MessageBox()
                {
                    Title = "内存清理",
                    Content = "清理成功"
                }.ShowDialogAsync();
            }
            catch (Exception ex)
            {
                {
                    await new Wpf.Ui.Controls.MessageBox()
                    {
                        Title = "内存清理",
                        Content = $"清理失败，{ex.Message}"
                    }.ShowDialogAsync();
                }
            }
        }
    }
}