using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GUI_Library
{
    /// <summary>
    /// true  -> green, false -> red. Used for status and pass/fail badge. 
    /// </summary>
    public class BoolToStatusBrush : IValueConverter
    {
        private static readonly SolidColorBrush Green =
            new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));   // green
        private static readonly SolidColorBrush Red =
            new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));   // red

        public object Convert(object value, Type targetType,
                              object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            return b ? Green : Red;
        }

        public object ConvertBack(object value, Type targetType,
                                  object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
