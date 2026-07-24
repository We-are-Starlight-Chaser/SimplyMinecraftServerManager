// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using CommunityToolkit.Mvvm.Messaging;
using SimplyMinecraftServerManager.Internals;
using SimplyMinecraftServerManager.ViewModels.Windows;
using Wpf.Ui.Controls;
using static SimplyMinecraftServerManager.ViewModels.Windows.FirstTimeUsingWindowViewModel;

namespace SimplyMinecraftServerManager.Views.Windows
{
    public partial class FirstTimeUsingWindow : FluentWindow
    {
        public FirstTimeUsingWindowViewModel ViewModel { get; set; }
        public FirstTimeUsingWindow(FirstTimeUsingWindowViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
            WeakReferenceMessenger.Default.Register<FirstTimeUsingCompletedMessage>(this, OnTutorialCompleted);
            Closed += FirstTimeUsingWindow_Closed;
        }
        private void OnTutorialCompleted(object recipient, FirstTimeUsingCompletedMessage message)
        {
            Close();
        }
        private void FirstTimeUsingWindow_Closed(object? sender, EventArgs e)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            Closed -= FirstTimeUsingWindow_Closed;
        }
    }
}
