// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Views.Pages
{
    /// <summary>
    /// ScheduledTaskDialog.xaml 的交互逻辑
    /// </summary>
    public partial class ScheduledTaskDialog : ContentDialog, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private string _commands;
        public string Commands
        {
            get { return _commands; }
            set
            {
                _commands = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Commands)));
            }
        }
        public ScheduledTaskDialog(string commands)
        {
            InitializeComponent();
            _commands = commands;
            DataContext = this;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            
            Hide();
        }
    }
}
