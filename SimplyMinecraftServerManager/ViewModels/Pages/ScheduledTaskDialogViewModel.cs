using System;
using System.Collections.Generic;
using System.Text;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class ScheduledTaskDialogViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _commands = "";
    }
}
