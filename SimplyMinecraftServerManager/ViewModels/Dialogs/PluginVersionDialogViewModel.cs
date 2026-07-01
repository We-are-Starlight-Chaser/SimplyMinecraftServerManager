// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Internals.Downloads;
using System.Collections.ObjectModel;
using System.Globalization;

namespace SimplyMinecraftServerManager.ViewModels.Dialogs
{
    /// <summary>
    /// 插件版本选择对话框的视图模型，支持分页加载和版本选择。
    /// </summary>
    public partial class PluginVersionDialogViewModel : ObservableObject
    {
        /// <summary>每页加载的版本数量。</summary>
        private const int PageSize = 8;

        /// <summary>所有可用版本的完整列表。</summary>
        private List<PluginVersionListItem> _allVersions = [];

        /// <summary>项目（插件）标题。</summary>
        [ObservableProperty]
        private string _projectTitle = "";

        /// <summary>目标实例的描述信息。</summary>
        [ObservableProperty]
        private string _targetDescription = "";

        /// <summary>确认按钮的显示文本。</summary>
        [ObservableProperty]
        private string _confirmButtonText = "安装";

        /// <summary>当前已加载到界面的版本列表。</summary>
        [ObservableProperty]
        private ObservableCollection<PluginVersionListItem> _versions = [];

        /// <summary>当前选中的版本项。</summary>
        [ObservableProperty]
        private PluginVersionListItem? _selectedVersionItem;

        /// <summary>指示是否还有更多版本可供加载。</summary>
        [ObservableProperty]
        private bool _hasMoreVersions;

        /// <summary>获取版本加载状态的显示文本。</summary>
        public string VersionLoadStatus => $"已显示 {Versions.Count} / {_allVersions.Count} 个版本";

        /// <summary>获取是否有可用版本。</summary>
        public bool HasVersions => Versions.Count > 0;

        /// <summary>
        /// 创建插件版本对话框视图模型实例。
        /// </summary>
        /// <param name="projectTitle">项目标题。</param>
        /// <param name="targetDescription">目标实例描述。</param>
        /// <param name="confirmButtonText">确认按钮文本。</param>
        /// <param name="versions">可用的 Modrinth 版本集合。</param>
        /// <returns>初始化完成的视图模型实例。</returns>
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

        /// <summary>
        /// 加载下一页的版本数据到界面列表中。
        /// </summary>
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

    /// <summary>
    /// 插件版本列表项，封装单个 Modrinth 版本的显示信息。
    /// </summary>
    public sealed class PluginVersionListItem
    {
        /// <summary>
        /// 使用 Modrinth 版本数据初始化列表项。
        /// </summary>
        /// <param name="version">Modrinth 版本信息。</param>
        public PluginVersionListItem(ModrinthVersion version)
        {
            Version = version;
        }

        /// <summary>获取原始 Modrinth 版本数据。</summary>
        public ModrinthVersion Version { get; }

        /// <summary>获取版本的显示名称，若为空则使用版本号。</summary>
        public string Name => string.IsNullOrWhiteSpace(Version.Name) ? Version.VersionNumber : Version.Name;

        /// <summary>获取版本号文本，若为空则显示"未命名版本"。</summary>
        public string VersionNumber => string.IsNullOrWhiteSpace(Version.VersionNumber) ? "未命名版本" : Version.VersionNumber;

        /// <summary>获取版本类型文本（如 Release、Beta 等）。</summary>
        public string VersionTypeText => string.IsNullOrWhiteSpace(Version.VersionType)
            ? "Release"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Version.VersionType.ToLowerInvariant());

        /// <summary>获取版本的发布日期格式化文本。</summary>
        public string PublishedText
        {
            get
            {
                var published = ParsePublishedDate(Version.DatePublished);
                return published == DateTime.MinValue ? "发布时间未知" : published.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
        }

        /// <summary>获取下载次数的格式化文本。</summary>
        public string DownloadsText => FormatNumber(Version.Downloads);

        /// <summary>获取主文件的文件名。</summary>
        public string FileName => Version.PrimaryFile?.FileName ?? "无可用文件";

        /// <summary>获取主文件的大小（字节）。</summary>
        public long FileSizeBytes => Version.PrimaryFile?.Size ?? 0;

        /// <summary>获取支持的加载器列表文本。</summary>
        public string LoadersText => JoinOrPlaceholder(Version.Loaders, "未标注加载器");

        /// <summary>获取支持的游戏版本范围文本。</summary>
        public string GameVersionsText => FormatGameVersionRange(Version.GameVersions);

        /// <summary>获取更新说明的预览文本（最多 1200 字符）。</summary>
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

        /// <summary>
        /// 解析发布日期字符串为 DateTime 对象。
        /// </summary>
        /// <param name="value">ISO 8601 格式的日期字符串。</param>
        /// <returns>解析成功返回对应的 DateTime，失败返回 DateTime.MinValue。</returns>
        internal static DateTime ParsePublishedDate(string? value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var published)
                ? published
                : DateTime.MinValue;
        }

        /// <summary>
        /// 将字符串集合用逗号连接，若为空则返回占位符。
        /// </summary>
        private static string JoinOrPlaceholder(IReadOnlyCollection<string>? values, string placeholder)
        {
            if (values == null || values.Count == 0)
            {
                return placeholder;
            }

            return string.Join(", ", values.Take(6));
        }

        /// <summary>
        /// 格式化游戏版本范围，如 "1.20.1 - 1.21.4"。
        /// </summary>
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

        /// <summary>
        /// 将版本号字符串解析为可比较的 Version 对象。
        /// </summary>
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

        /// <summary>
        /// 将数字格式化为带单位的缩写文本（如 1.2K、3.5M）。
        /// </summary>
        private static string FormatNumber(long value)
        {
            if (value >= 1_000_000_000) return $"{value / 1_000_000_000d:F1}B";
            if (value >= 1_000_000) return $"{value / 1_000_000d:F1}M";
            if (value >= 1_000) return $"{value / 1_000d:F1}K";
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
