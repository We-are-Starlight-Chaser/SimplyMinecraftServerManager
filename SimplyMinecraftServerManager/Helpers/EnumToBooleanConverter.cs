using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Appearance;

namespace SimplyMinecraftServerManager.Helpers
{
    /// <summary>
    /// 主题枚举转布尔值转换器（专门用于主题切换）
    /// 注意：WPF-UI 已内置通用的 EnumToBooleanConverter，此类用于特定主题场景
    /// </summary>
    internal class ThemeToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is not string enumString)
            {
                throw new ArgumentException("ExceptionThemeToBooleanConverterParameterMustBeAnEnumName");
            }

            if (!Enum.IsDefined(typeof(ApplicationTheme), value))
            {
                throw new ArgumentException("ExceptionThemeToBooleanConverterValueMustBeAnEnum");
            }

            var enumValue = Enum.Parse(typeof(ApplicationTheme), enumString);

            return enumValue.Equals(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is not string enumString)
            {
                throw new ArgumentException("ExceptionThemeToBooleanConverterParameterMustBeAnEnumName");
            }

            return Enum.Parse(typeof(ApplicationTheme), enumString);
        }
    }
}