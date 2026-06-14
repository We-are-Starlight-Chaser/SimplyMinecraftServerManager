using System;
using System.Collections.Generic;
using System.Text;

namespace SimplyMinecraftServerManager.Extension.Interfaces
{
    public interface ICustomTool
    {
        object ControlContent { get; set; }
        Task RunAsync(CancellationToken cancellationToken);
    }
}
