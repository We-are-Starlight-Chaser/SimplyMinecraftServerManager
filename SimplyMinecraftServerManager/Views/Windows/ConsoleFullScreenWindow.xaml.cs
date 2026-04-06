using SimplyMinecraftServerManager.ViewModels.Pages;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace SimplyMinecraftServerManager.Views.Windows
{
    public partial class ConsoleFullScreenWindow : Window
    {
        private readonly InstanceViewModel _viewModel;
        private bool _isClosingFromViewModel;

        public ConsoleFullScreenWindow(InstanceViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            InitializeComponent();

            Loaded += OnLoaded;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.IsConsoleFullScreen = true;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            Loaded -= OnLoaded;
            Closed -= OnClosed;
            PreviewKeyDown -= OnPreviewKeyDown;

            if (!_isClosingFromViewModel)
            {
                _viewModel.IsConsoleFullScreen = false;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(InstanceViewModel.IsConsoleFullScreen))
            {
                return;
            }

            if (_viewModel.IsConsoleFullScreen)
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(CloseFromViewModel);
                return;
            }

            CloseFromViewModel();
        }

        private void CloseFromViewModel()
        {
            if (!IsLoaded)
            {
                return;
            }

            _isClosingFromViewModel = true;
            Close();
        }
    }
}
