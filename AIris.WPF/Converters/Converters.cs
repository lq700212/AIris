using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIris.WPF.Converters
{
    /// <summary>布尔取反 → 可见性（设计稿里"启用中/已停用"胶囊二选一显示用）。</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool && (bool)value;
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
