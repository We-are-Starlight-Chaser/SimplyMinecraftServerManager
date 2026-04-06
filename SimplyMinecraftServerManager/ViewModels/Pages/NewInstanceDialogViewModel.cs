using SimplyMinecraftServerManager.Internals;
using System.Collections.ObjectModel;
using Wpf.Ui.Abstractions.Controls;
using Microsoft.Win32;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace SimplyMinecraftServerManager.ViewModels.Pages
{
    public partial class NewInstanceDialogViewModel : ObservableObject
    {
        private readonly ObservableCollection<string> _availableVersions;
        private readonly ObservableCollection<JdkDisplayItem> _availableJdks;
        private readonly Func<Task> _onCreate;
        private readonly Action _onCancel;

        public NewInstanceDialogViewModel(
            ObservableCollection<string> availableVersions,
            ObservableCollection<JdkDisplayItem> availableJdks,
            Func<Task> onCreate,
            Action onCancel)
        {
            _availableVersions = availableVersions;
            _availableJdks = availableJdks;
            _onCreate = onCreate;
            _onCancel = onCancel;

            if (_availableVersions.Count > 0)
            {
                SelectedVersion = _availableVersions[0];
            }

            if (_availableJdks.Count > 0)
            {
                SelectedJdk = _availableJdks[0];
            }
        }

        [ObservableProperty]
        private string _instanceName = "";

        [ObservableProperty]
        private string _serverType = "paper";

        [ObservableProperty]
        private string? _selectedVersion;

        [ObservableProperty]
        private string? _customJarPath = "";

        [ObservableProperty]
        private bool _useCustomJar = false;

        [ObservableProperty]
        private JdkDisplayItem? _selectedJdk;

        [ObservableProperty]
        private int _minMemory = 1024;

        [ObservableProperty]
        private int _maxMemory = 2048;

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        private bool _isCreating = false;

        public ObservableCollection<string> AvailableVersions => _availableVersions;
        public ObservableCollection<JdkDisplayItem> AvailableJdks => _availableJdks;

        public string[] ServerTypes => ["paper", "purpur", "leaves", "leaf"];

        [RelayCommand]
        private void BrowseCustomJar()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JAR 文件 (*.jar)|*.jar|所有文件 (*.*)|*.*",
                Title = "选择自定义服务端 JAR 文件",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                CustomJarPath = openFileDialog.FileName;
            }
        }

        public async Task CreateAsync()
        {
            await _onCreate();
        }

        public void Cancel()
        {
            _onCancel();
        }
    }
}
