// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SimplyMinecraftServerManager.Helpers
{
    internal class InverseBooleanConverter : IValueConverter
    {
        public static readonly InverseBooleanConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool boolValue ? !boolValue : true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool boolValue ? !boolValue : false;
    }

    internal class BooleanToVisibilityConverter : IValueConverter
    {
        public static readonly BooleanToVisibilityConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool boolValue && boolValue ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility visibility && visibility == Visibility.Visible;
    }

    internal class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public static readonly InverseBooleanToVisibilityConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool boolValue && boolValue ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not Visibility visibility || visibility != Visibility.Visible;
    }

    internal class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string str && !string.IsNullOrEmpty(str) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool inverse = parameter as string == "inverse";
            if (value is int count)
            {
                bool hasItems = count > 0;
                if (inverse) hasItems = !hasItems;
                return hasItems ? Visibility.Visible : Visibility.Collapsed;
            }
            return inverse ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class FileSizeConverter : IValueConverter
    {
        private static readonly string[] Suffixes = ["B", "KB", "MB", "GB", "TB"];

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                int i = 0;
                double size = bytes;
                while (size >= 1024 && i < Suffixes.Length - 1)
                {
                    size /= 1024;
                    i++;
                }
                return $"{size:F2} {Suffixes[i]}";
            }
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class PlatformNameToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush PaperLight = new(Color.FromRgb(0xC9, 0xA2, 0x27));
        private static readonly SolidColorBrush PaperDark = new(Color.FromRgb(0xE8, 0xC5, 0x47));
        private static readonly SolidColorBrush FoliaLight = new(Color.FromRgb(0x3D, 0x8B, 0x40));
        private static readonly SolidColorBrush FoliaDark = new(Color.FromRgb(0x5C, 0xB8, 0x5C));
        private static readonly SolidColorBrush PurpurLight = new(Color.FromRgb(0x7B, 0x1F, 0xA2));
        private static readonly SolidColorBrush PurpurDark = new(Color.FromRgb(0x9C, 0x27, 0xB0));
        private static readonly SolidColorBrush LeavesLight = new(Color.FromRgb(0x55, 0x8B, 0x2F));
        private static readonly SolidColorBrush LeavesDark = new(Color.FromRgb(0x7C, 0xB3, 0x42));
        private static readonly SolidColorBrush LeafLight = new(Color.FromRgb(0x00, 0x97, 0xA7));
        private static readonly SolidColorBrush LeafDark = new(Color.FromRgb(0x00, 0xBC, 0xD4));
        private static readonly SolidColorBrush DefaultLight = new(Color.FromRgb(0x60, 0x7D, 0x8B));
        private static readonly SolidColorBrush DefaultDark = new(Color.FromRgb(0x78, 0x90, 0x9C));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            return (value as string, isDark) switch
            {
                ("Paper", false) => PaperLight, ("Paper", true) => PaperDark,
                ("Folia", false) => FoliaLight, ("Folia", true) => FoliaDark,
                ("Purpur", false) => PurpurLight, ("Purpur", true) => PurpurDark,
                ("Leaves", false) => LeavesLight, ("Leaves", true) => LeavesDark,
                ("Leaf", false) => LeafLight, ("Leaf", true) => LeafDark,
                (_, false) => DefaultLight, (_, true) => DefaultDark
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class ThemeAwareColorConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush GrayBrush = new(Colors.Gray);
        private static readonly Dictionary<string, SolidColorBrush> _brushCache = [];

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is string lightColor && values[1] is string darkColor)
            {
                bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
                string colorHex = isDark ? darkColor : lightColor;
                if (colorHex.StartsWith('#') && colorHex.Length == 7)
                {
                    if (!_brushCache.TryGetValue(colorHex, out var brush))
                    {
                        brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)!);
                        _brushCache[colorHex] = brush;
                    }
                    return brush;
                }
            }
            return GrayBrush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class PlatformNameToInitialConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string name && !string.IsNullOrEmpty(name))
            {
                return name[0] switch
                {
                    'P' when name.Length > 4 => "P",
                    'F' => "F",
                    'L' when name is "Leaves" or "Leaf" => "L",
                    _ => name[0].ToString()
                };
            }
            return "?";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class JdkStatusToBackgroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush ValidLight = new(Color.FromRgb(0xE8, 0xF5, 0xE9));
        private static readonly SolidColorBrush ValidDark = new(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly SolidColorBrush InvalidLight = new(Color.FromRgb(0xFF, 0xEB, 0xEE));
        private static readonly SolidColorBrush InvalidDark = new(Color.FromRgb(0xC6, 0x28, 0x28));
        private static readonly SolidColorBrush DefaultLight = new(Color.FromRgb(0xF5, 0xF5, 0xF5));
        private static readonly SolidColorBrush DefaultDark = new(Color.FromRgb(0x42, 0x42, 0x42));
        private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            if (value is string status)
            {
                return (status, isDark) switch
                {
                    ("Valid", false) => ValidLight, ("Valid", true) => ValidDark,
                    ("Invalid", false) => InvalidLight, ("Invalid", true) => InvalidDark,
                    (_, false) => DefaultLight, (_, true) => DefaultDark
                };
            }
            return Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class JdkStatusToForegroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush ValidLight = new(Color.FromRgb(0x1B, 0x5E, 0x20));
        private static readonly SolidColorBrush ValidDark = new(Color.FromRgb(0x81, 0xC7, 0x84));
        private static readonly SolidColorBrush InvalidLight = new(Color.FromRgb(0xB7, 0x1C, 0x1C));
        private static readonly SolidColorBrush InvalidDark = new(Color.FromRgb(0xEF, 0x9A, 0x9A));
        private static readonly SolidColorBrush DefaultLight = new(Color.FromRgb(0x61, 0x61, 0x61));
        private static readonly SolidColorBrush DefaultDark = new(Color.FromRgb(0xBD, 0xBD, 0xBD));
        private static readonly SolidColorBrush GrayBrush = new(Colors.Gray);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            if (value is string status)
            {
                return (status, isDark) switch
                {
                    ("Valid", false) => ValidLight, ("Valid", true) => ValidDark,
                    ("Invalid", false) => InvalidLight, ("Invalid", true) => InvalidDark,
                    (_, false) => DefaultLight, (_, true) => DefaultDark
                };
            }
            return GrayBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class RunningToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool isRunning && isRunning
                ? SymbolRegular.PlayCircle24
                : SymbolRegular.CircleSmall24;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class NotificationAppearanceToBadgeBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush SuccessLight = new(Color.FromRgb(0xE5, 0xF6, 0xEA));
        private static readonly SolidColorBrush SuccessDark = new(Color.FromRgb(0x1E, 0x5E, 0x38));
        private static readonly SolidColorBrush DangerLight = new(Color.FromRgb(0xFD, 0xEA, 0xEA));
        private static readonly SolidColorBrush DangerDark = new(Color.FromRgb(0x7A, 0x22, 0x22));
        private static readonly SolidColorBrush CautionLight = new(Color.FromRgb(0xFD, 0xF2, 0xD9));
        private static readonly SolidColorBrush CautionDark = new(Color.FromRgb(0x74, 0x54, 0x10));
        private static readonly SolidColorBrush InfoLight = new(Color.FromRgb(0xE4, 0xF1, 0xFB));
        private static readonly SolidColorBrush InfoDark = new(Color.FromRgb(0x18, 0x4E, 0x77));
        private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            if (value is ControlAppearance appearance)
            {
                return (appearance, isDark) switch
                {
                    (ControlAppearance.Success, false) => SuccessLight, (ControlAppearance.Success, true) => SuccessDark,
                    (ControlAppearance.Danger, false) => DangerLight, (ControlAppearance.Danger, true) => DangerDark,
                    (ControlAppearance.Caution, false) => CautionLight, (ControlAppearance.Caution, true) => CautionDark,
                    (_, false) => InfoLight, (_, true) => InfoDark
                };
            }
            return Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class NotificationAppearanceToForegroundBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush SuccessLight = new(Color.FromRgb(0x1D, 0x6F, 0x42));
        private static readonly SolidColorBrush SuccessDark = new(Color.FromRgb(0x8B, 0xE2, 0xA8));
        private static readonly SolidColorBrush DangerLight = new(Color.FromRgb(0xB4, 0x23, 0x18));
        private static readonly SolidColorBrush DangerDark = new(Color.FromRgb(0xFF, 0xB3, 0xB3));
        private static readonly SolidColorBrush CautionLight = new(Color.FromRgb(0x8F, 0x61, 0x00));
        private static readonly SolidColorBrush CautionDark = new(Color.FromRgb(0xFF, 0xD8, 0x75));
        private static readonly SolidColorBrush InfoLight = new(Color.FromRgb(0x0E, 0x63, 0xA9));
        private static readonly SolidColorBrush InfoDark = new(Color.FromRgb(0x9A, 0xD3, 0xFF));
        private static readonly SolidColorBrush GrayBrush = new(Colors.Gray);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            if (value is ControlAppearance appearance)
            {
                return (appearance, isDark) switch
                {
                    (ControlAppearance.Success, false) => SuccessLight, (ControlAppearance.Success, true) => SuccessDark,
                    (ControlAppearance.Danger, false) => DangerLight, (ControlAppearance.Danger, true) => DangerDark,
                    (ControlAppearance.Caution, false) => CautionLight, (ControlAppearance.Caution, true) => CautionDark,
                    (_, false) => InfoLight, (_, true) => InfoDark
                };
            }
            return GrayBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class NotificationAppearanceToBorderBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush DefaultBorder = new(Color.FromArgb(0x22, 0x7F, 0x7F, 0x7F));
        private static readonly SolidColorBrush SuccessBorder = new(Color.FromArgb(0x66, 0x1D, 0x6F, 0x42));
        private static readonly SolidColorBrush DangerBorder = new(Color.FromArgb(0x66, 0xB4, 0x23, 0x18));
        private static readonly SolidColorBrush CautionBorder = new(Color.FromArgb(0x66, 0x8F, 0x61, 0x00));
        private static readonly SolidColorBrush InfoBorder = new(Color.FromArgb(0x66, 0x0E, 0x63, 0xA9));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ControlAppearance appearance
                ? appearance switch
                {
                    ControlAppearance.Success => SuccessBorder,
                    ControlAppearance.Danger => DangerBorder,
                    ControlAppearance.Caution => CautionBorder,
                    _ => InfoBorder
                }
                : DefaultBorder;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class NotificationAppearanceToSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ControlAppearance appearance
                ? appearance switch
                {
                    ControlAppearance.Success => SymbolRegular.CheckmarkCircle24,
                    ControlAppearance.Danger => SymbolRegular.ErrorCircle24,
                    ControlAppearance.Caution => SymbolRegular.Warning24,
                    _ => SymbolRegular.Info24
                }
                : SymbolRegular.Info24;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class BooleanToEnableTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool isEnabled && isEnabled ? "禁用" : "启用";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class BooleanToAppearanceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool isEnabled && isEnabled
                ? ControlAppearance.Caution
                : ControlAppearance.Success;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class StringToUpperConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string str ? str.ToUpperInvariant() : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class ListToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IReadOnlyList<string> list && list.Count > 0)
            {
                int take = Math.Min(list.Count, 5);
                return string.Join(", ", list.Take(take));
            }
            return "无";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class GameVersionsToRangeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IReadOnlyList<string> list && list.Count > 0)
            {
                string min = list[0], max = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    if (string.Compare(list[i], min, StringComparison.Ordinal) < 0) min = list[i];
                    if (string.Compare(list[i], max, StringComparison.Ordinal) > 0) max = list[i];
                }
                return min == max ? min : $"{min}-{max}";
            }
            return "无";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class MultiBooleanConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => values.Length < 2 || (!(bool)values[0] && !(bool)values[1]);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class ScheduledTaskIndexToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is int idx && values[1] is string name)
            {
                if (idx == 0 && name == "ExecuteCommand") return Visibility.Visible;
                if (idx == 1 && name == "Backup") return Visibility.Visible;
                if (idx == 2 && name == "Restart") return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
