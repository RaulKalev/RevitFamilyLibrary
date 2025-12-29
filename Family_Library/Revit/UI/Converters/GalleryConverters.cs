using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Linq;

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

    /// <summary>
    /// Loads an image from a file path into memory, releasing the file handle immediately.
    /// This prevents WPF from locking the file while the image is displayed.
    /// </summary>
    public class PathToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string rawPath && !string.IsNullOrWhiteSpace(rawPath))
            {
                try
                {
                    // Strip query parameter if present (e.g. ?t=123456)
                    var path = rawPath;
                    int idx = path.IndexOf('?');
                    if (idx >= 0) path = path.Substring(0, idx);

                    if (File.Exists(path))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        // Use original rawPath here? No, use file path. 
                        // Actually BitmapImage(Uri) supports file paths. 
                        // But if we pass the query param to BitmapImage Uri source, local file loading might fail.
                        // However, we are initializing it manually.
                        // Let's stick to passing the clean path to UriSource.
                        // The "New BitmapImage" logic itself is what forces the reload effectively, 
                        // IF the Convert method is called. 
                        // And Convert IS called because the string bindings are different.
                        bitmap.UriSource = new Uri(path, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze(); 
                        return bitmap;
                    }
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
    
    public class TupleConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null) return null;
            return values.Clone(); // Returns object[] as is
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class IsCategoryAssignedConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // value[0] = LibraryItem
            // value[1] = Category Name (string)
            if (values.Length >= 2 && 
                values[0] is Family_Library.UI.Models.LibraryItem item && 
                values[1] is string category)
            {
                return item.UserCategories != null && item.UserCategories.Contains(category, StringComparer.OrdinalIgnoreCase);
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
