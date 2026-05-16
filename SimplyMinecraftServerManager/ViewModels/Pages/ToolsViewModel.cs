using CommunityToolkit.Mvvm.ComponentModel;
using SimplyMinecraftServerManager.Internals;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class ToolsViewModel : ObservableObject
    {
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