using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Family_Library.UI
{
    /// <summary>Negates a bool (e.g. disable an action while a task runs).</summary>
    public sealed class NotConverter : IValueConverter
    {
        public static readonly NotConverter Instance = new NotConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    }

    /// <summary>True when the value is not null.</summary>
    public sealed class NotNullConverter : IValueConverter
    {
        public static readonly NotNullConverter Instance = new NotNullConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value != null;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Sidebar count badge: shown for a positive count unless the sidebar is icon-only. [count, isCompact]</summary>
    public sealed class BadgeVisibility : IMultiValueConverter
    {
        public static readonly BadgeVisibility Instance = new BadgeVisibility();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var count = values.Length > 0 && values[0] is int i ? i : 0;
            var compact = values.Length > 1 && values[1] is bool b && b;
            return count > 0 && !compact ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
