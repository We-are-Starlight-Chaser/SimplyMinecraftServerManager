using SimplyMinecraftServerManager.Internals;
using System.Collections.ObjectModel;
using Wpf.Ui.Abstractions.Controls;

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
        }

        [ObservableProperty]
        private string _instanceName = "";

        [ObservableProperty]
        private string _serverType = "paper";

        [ObservableProperty]
        private string? _selectedVersion;

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
