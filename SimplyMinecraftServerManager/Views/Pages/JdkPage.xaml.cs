using SimplyMinecraftServerManager.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class JdkPage : INavigableView<JdkViewModel>
    {
        public JdkViewModel ViewModel { get; }

        public JdkPage(JdkViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
