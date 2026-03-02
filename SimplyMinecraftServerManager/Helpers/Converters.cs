using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace SimplyMinecraftServerManager.Helpers
{
    /// <summary>
    /// 布尔值反转转换器
    /// </summary>
    internal class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }

    /// <summary>
    /// 布尔值转可见性转换器
    /// </summary>
    internal class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    /// <summary>
    /// 布尔值反转转可见性转换器 (true 时隐藏，false 时显示)
    /// </summary>
    internal class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return true;
        }
    }

    /// <summary>
    /// 字符串转可见性转换器 (非空字符串显示)
    /// </summary>
    internal class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                return !string.IsNullOrEmpty(str) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 整数转可见性转换器 (大于0显示，等于0隐藏)
    /// 参数为 "inverse" 时反转逻辑
    /// </summary>
    internal class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool inverse = parameter?.ToString() == "inverse";

            if (value is int count)
            {
                bool hasItems = count > 0;
                if (inverse) hasItems = !hasItems;
                return hasItems ? Visibility.Visible : Visibility.Collapsed;
            }
            return inverse ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 文件大小格式化转换器
    /// </summary>
    internal class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
                int i = 0;
                double size = bytes;
                while (size >= 1024 && i < suffixes.Length - 1)
                {
                    size /= 1024;
                    i++;
                }
                return $"{size:F2} {suffixes[i]}";
            }
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 平台名称到颜色转换器
    /// </summary>
    internal class PlatformNameToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 检测当前主题
            var isDark = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;

            var (lightColor, darkColor) = value switch
            {
                "Paper" => (Color.FromRgb(0xC9, 0xA2, 0x27), Color.FromRgb(0xE8, 0xC5, 0x47)),
                "Folia" => (Color.FromRgb(0x3D, 0x8B, 0x40), Color.FromRgb(0x5C, 0xB8, 0x5C)),
                "Purpur" => (Color.FromRgb(0x7B, 0x1F, 0xA2), Color.FromRgb(0x9C, 0x27, 0xB0)),
                "Leaves" => (Color.FromRgb(0x55, 0x8B, 0x2F), Color.FromRgb(0x7C, 0xB3, 0x42)),
                "Leaf" => (Color.FromRgb(0x00, 0x97, 0xA7), Color.FromRgb(0x00, 0xBC, 0xD4)),
                _ => (Color.FromRgb(0x60, 0x7D, 0x8B), Color.FromRgb(0x78, 0x90, 0x9C))
            };

            return new SolidColorBrush(isDark ? darkColor : lightColor);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 多值转换器：根据主题选择颜色
    /// </summary>
    internal class ThemeAwareColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is string lightColor && values[1] is string darkColor)
            {
                var isDark = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;

                var colorHex = isDark ? darkColor : lightColor;
                if (colorHex.StartsWith("#") && colorHex.Length == 7)
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorHex);
                    return new SolidColorBrush(color);
                }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 平台名称到首字母转换器
    /// </summary>
    internal class PlatformNameToInitialConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string name && !string.IsNullOrEmpty(name))
            {
                // 特殊表情
                return name switch
                {
                    "Paper" => "P",
                    "Folia" => "F",
                    "Purpur" => "P",
                    "Leaves" => "L",
                    "Leaf" => "L",
                    _ => name[0].ToString()
                };
            }
            return "?";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 数量为0时显示转换器
    /// </summary>
    internal class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// JDK 状态到背景色转换器
    /// </summary>
    internal class JdkStatusToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

            if (value is string status)
            {
                return status switch
                {
                    "Valid" => new SolidColorBrush(isDark
                        ? Color.FromRgb(0x2E, 0x7D, 0x32)   // 深色模式：绿色
                        : Color.FromRgb(0xE8, 0xF5, 0xE9)), // 浅色模式：很浅的绿
                    "Invalid" => new SolidColorBrush(isDark
                        ? Color.FromRgb(0xC6, 0x28, 0x28)   // 深色模式：红色
                        : Color.FromRgb(0xFF, 0xEB, 0xEE)), // 浅色模式：很浅的红
                    _ => new SolidColorBrush(isDark
                        ? Color.FromRgb(0x42, 0x42, 0x42)
                        : Color.FromRgb(0xF5, 0xF5, 0xF5))
                };
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// JDK 状态到前景色转换器
    /// </summary>
    internal class JdkStatusToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

            if (value is string status)
            {
                return status switch
                {
                    "Valid" => new SolidColorBrush(isDark
                        ? Color.FromRgb(0x81, 0xC7, 0x84)   // 深色模式：亮绿
                        : Color.FromRgb(0x1B, 0x5E, 0x20)), // 浅色模式：深绿
                    "Invalid" => new SolidColorBrush(isDark
                        ? Color.FromRgb(0xEF, 0x9A, 0x9A)   // 深色模式：亮红
                        : Color.FromRgb(0xB7, 0x1C, 0x1C)), // 浅色模式：深红
                    _ => new SolidColorBrush(isDark
                        ? Color.FromRgb(0xBD, 0xBD, 0xBD)
                        : Color.FromRgb(0x61, 0x61, 0x61))
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 运行状态到图标转换器
    /// </summary>
    internal class RunningToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isRunning)
            {
                // 返回 Symbol 枚举值
                return isRunning ? Wpf.Ui.Controls.SymbolRegular.PlayCircle24 : Wpf.Ui.Controls.SymbolRegular.CircleSmall24;
            }
            return Wpf.Ui.Controls.SymbolRegular.CircleSmall24;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
