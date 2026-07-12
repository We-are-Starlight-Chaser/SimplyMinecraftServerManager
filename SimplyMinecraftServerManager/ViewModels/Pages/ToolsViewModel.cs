// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using CommunityToolkit.Mvvm.ComponentModel;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.ViewModels.Dialogs;
using SimplyMinecraftServerManager.Views.Pages;
using Wpf.Ui;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    /// <summary>
    /// 工具页面的视图模型，提供系统工具功能
    /// </summary>
    public partial class ToolsViewModel(IContentDialogService contentDialogService) : ObservableObject
    {
        private readonly IContentDialogService contentDialogService = contentDialogService;
        private readonly ServerPingerDialogViewModel _pingerDialogViewModel = new();

        /// <summary>
        /// 清理系统内存，释放未使用的内存资源
        /// </summary>
        [RelayCommand]
        private async Task CleanMemoryAsync()
        {
            try
            {
                await Task.Run(()=>new MemoryCleaner().CleanMemory());
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
        [RelayCommand]
        private async Task OpenPingerContentDialogAsync()
        {
            await contentDialogService.ShowAsync(new ServerPingerDialog(_pingerDialogViewModel), CancellationToken.None);
        }
    }
}