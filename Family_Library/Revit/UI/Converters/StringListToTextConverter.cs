using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Family_Library.UI.Converters
{
    public class StringListToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var list = value as IEnumerable<string>;
            if (list == null) return "";

            // show first N to avoid huge rows
            var items = list.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (items.Count == 0) return "";

            return string.Join(", ", items);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
