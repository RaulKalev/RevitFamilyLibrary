using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Family_Library.UI.Converters
{
    public class HoverAndMultiThumbsToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isHover = values.Length > 0 && values[0] is bool b0 && b0;
            bool hasMany = values.Length > 1 && values[1] is bool b1 && b1;
            return (isHover && hasMany) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class PlusOneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i) return i + 1;
            return 1;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
