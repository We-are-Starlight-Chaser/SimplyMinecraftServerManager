using SimplyMinecraftServerManager.Internals.Downloads;
using System.Collections.ObjectModel;
using System.Globalization;

namespace SimplyMinecraftServerManager.ViewModels.Dialogs
{
    public partial class PluginVersionDialogViewModel : ObservableObject
    {
        private const int PageSize = 8;
        private List<PluginVersionListItem> _allVersions = [];

        [ObservableProperty]
        private string _projectTitle = "";

        [ObservableProperty]
        private string _targetDescription = "";

        [ObservableProperty]
        private string _confirmButtonText = "安装";

        [ObservableProperty]
        private ObservableCollection<PluginVersionListItem> _versions = [];

        [ObservableProperty]
        private PluginVersionListItem? _selectedVersionItem;

        [ObservableProperty]
        private bool _hasMoreVersions;

        public string VersionLoadStatus => $"已显示 {Versions.Count} / {_allVersions.Count} 个版本";

        public bool HasVersions => Versions.Count > 0;

        public static PluginVersionDialogViewModel Create(
            string projectTitle,
            string targetDescription,
            string confirmButtonText,
            IEnumerable<ModrinthVersion> versions)
        {
            var orderedItems = versions
                .OrderByDescending(static version => PluginVersionListItem.ParsePublishedDate(version.DatePublished))
                .ThenByDescending(static version => version.VersionNumber, StringComparer.OrdinalIgnoreCase)
                .Select(static version => new PluginVersionListItem(version))
                .ToList();

            var viewModel = new PluginVersionDialogViewModel
            {
                ProjectTitle = projectTitle,
                TargetDescription = targetDescription,
                ConfirmButtonText = confirmButtonText
            };

            viewModel._allVersions = orderedItems;
            viewModel.LoadMoreVersions();
            return viewModel;
        }

        [RelayCommand]
        private void LoadMoreVersions()
        {
            if (_allVersions.Count == 0)
            {
                HasMoreVersions = false;
                return;
            }

            int currentCount = Versions.Count;
            int nextCount = Math.Min(currentCount + PageSize, _allVersions.Count);

            for (int i = currentCount; i < nextCount; i++)
            {
                Versions.Add(_allVersions[i]);
            }

            if (SelectedVersionItem == null)
            {
                SelectedVersionItem = Versions.FirstOrDefault();
            }

            HasMoreVersions = Versions.Count < _allVersions.Count;
            OnPropertyChanged(nameof(VersionLoadStatus));
        }
    }

    public sealed class PluginVersionListItem
    {
        public PluginVersionListItem(ModrinthVersion version)
        {
            Version = version;
        }

        public ModrinthVersion Version { get; }

        public string Name => string.IsNullOrWhiteSpace(Version.Name) ? Version.VersionNumber : Version.Name;

        public string VersionNumber => string.IsNullOrWhiteSpace(Version.VersionNumber) ? "未命名版本" : Version.VersionNumber;

        public string VersionTypeText => string.IsNullOrWhiteSpace(Version.VersionType)
            ? "Release"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Version.VersionType.ToLowerInvariant());

        public string PublishedText
        {
            get
            {
                var published = ParsePublishedDate(Version.DatePublished);
                return published == DateTime.MinValue ? "发布时间未知" : published.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
        }

        public string DownloadsText => FormatNumber(Version.Downloads);

        public string FileName => Version.PrimaryFile?.FileName ?? "无可用文件";

        public long FileSizeBytes => Version.PrimaryFile?.Size ?? 0;

        public string LoadersText => JoinOrPlaceholder(Version.Loaders, "未标注加载器");

        public string GameVersionsText => FormatGameVersionRange(Version.GameVersions);

        public string ChangelogPreview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Version.Changelog))
                {
                    return "此版本没有提供更新说明。";
                }

                var normalized = Version.Changelog.Replace("\r\n", "\n").Trim();
                return normalized.Length <= 1200 ? normalized : $"{normalized[..1200]}...";
            }
        }

        internal static DateTime ParsePublishedDate(string? value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var published)
                ? published
                : DateTime.MinValue;
        }

        private static string JoinOrPlaceholder(IReadOnlyCollection<string>? values, string placeholder)
        {
            if (values == null || values.Count == 0)
            {
                return placeholder;
            }

            return string.Join(", ", values.Take(6));
        }

        private static string FormatGameVersionRange(IReadOnlyCollection<string>? versions)
        {
            if (versions == null || versions.Count == 0)
            {
                return "未标注版本";
            }

            var ordered = versions
                .Where(static version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static version => ParseComparableVersion(version))
                .ToList();

            if (ordered.Count == 0)
            {
                return "未标注版本";
            }

            if (ordered.Count == 1)
            {
                return ordered[0];
            }

            return $"{ordered[0]} - {ordered[^1]}";
        }

        private static System.Version ParseComparableVersion(string version)
        {
            var normalized = version;
            var dashIndex = normalized.IndexOf('-');
            if (dashIndex > 0)
            {
                normalized = normalized[..dashIndex];
            }

            return System.Version.TryParse(normalized, out var parsed)
                ? parsed
                : new System.Version(0, 0);
        }

        private static string FormatNumber(long value)
        {
            if (value >= 1_000_000_000) return $"{value / 1_000_000_000d:F1}B";
            if (value >= 1_000_000) return $"{value / 1_000_000d:F1}M";
            if (value >= 1_000) return $"{value / 1_000d:F1}K";
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
