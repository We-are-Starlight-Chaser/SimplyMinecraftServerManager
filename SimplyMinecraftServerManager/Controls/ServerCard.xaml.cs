using System.Windows.Controls;
namespace SimplyMinecraftServerManager.Controls
{
    public partial class ServerCard : UserControl
    {
        /// <param name="sn">Server Name</param>
        public ServerCard(string sn)
        {
            InitializeComponent();
            ServerName.Text = sn;
        }
    }
}
