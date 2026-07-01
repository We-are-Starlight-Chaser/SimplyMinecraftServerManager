// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

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
        /// <summary>
        /// 将主题枚举值转换为布尔值，用于绑定 RadioButton 的 IsChecked 属性
        /// </summary>
        /// <param name="value">当前绑定源的主题枚举值</param>
        /// <param name="targetType">目标绑定属性类型</param>
        /// <param name="parameter">要比较的目标枚举名称字符串</param>
        /// <param name="culture">区域化信息</param>
        /// <returns>当枚举值与参数匹配时返回 <c>true</c>，否则返回 <c>false</c></returns>
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

            var enumValue = Enum.Parse<ApplicationTheme>(enumString);

            return enumValue.Equals(value);
        }

        /// <summary>
        /// 将布尔值转换回对应的主题枚举值，用于 RadioButton 选中状态变更时更新绑定源
        /// </summary>
        /// <param name="value">RadioButton 的 IsChecked 值</param>
        /// <param name="targetType">目标绑定属性类型</param>
        /// <param name="parameter">要转换的目标枚举名称字符串</param>
        /// <param name="culture">区域化信息</param>
        /// <returns>转换后的 <see cref="ApplicationTheme"/> 枚举值</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is not string enumString)
            {
                throw new ArgumentException("ExceptionThemeToBooleanConverterParameterMustBeAnEnumName");
            }

            return Enum.Parse<ApplicationTheme>(enumString);
        }
    }
}