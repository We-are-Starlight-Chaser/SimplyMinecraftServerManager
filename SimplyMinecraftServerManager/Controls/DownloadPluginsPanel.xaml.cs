// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SimplyMinecraftServerManager.Controls
{
    public partial class DownloadPluginsPanel : UserControl
    {
        private readonly DispatcherTimer _searchDebounce;
        private string _lastSearchQuery = "";

        public DownloadPluginsPanel()
        {
            InitializeComponent();
            _searchDebounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchDebounce.Tick += OnDebounceTick;
        }

        private void OnPluginSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is DownloadViewModel viewModel)
            {
                _searchDebounce.Stop();
                viewModel.SearchPluginsCommand.Execute(null);
            }
        }

        private void OnSearchQueryChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is not DownloadViewModel viewModel) return;
            if (viewModel.PluginSearchQuery == _lastSearchQuery) return;
            _lastSearchQuery = viewModel.PluginSearchQuery ?? "";
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void OnDebounceTick(object? sender, EventArgs e)
        {
            _searchDebounce.Stop();
            if (DataContext is DownloadViewModel viewModel && !string.IsNullOrWhiteSpace(viewModel.PluginSearchQuery))
            {
                viewModel.SearchPluginsCommand.Execute(null);
            }
        }
    }
}
