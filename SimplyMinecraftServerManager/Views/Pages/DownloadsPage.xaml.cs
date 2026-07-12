// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows;
using Wpf.Ui.Abstractions.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    public partial class DownloadsPage : INavigableView<DownloadsViewModel>, IDisposable
    {
        public DownloadsViewModel ViewModel { get; }
        private bool _disposed;

        public DownloadsPage(DownloadsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ViewModel.TaskCountChanged += OnTaskCountChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.TaskCountChanged -= OnTaskCountChanged;
        }

        private void OnTaskCountChanged(object? sender, int count)
        {
            if (Application.Current.MainWindow is Views.Windows.MainWindow mainWindow)
            {
                mainWindow.ViewModel.UpdateDownloadTaskBadge(count);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ViewModel.TaskCountChanged -= OnTaskCountChanged;
            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
            GC.SuppressFinalize(this);
        }
    }
}
