using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GUI_Library
{
    /// <summary>true -> Visible, false -> Collapsed</summary>
    public class BoolToVis : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>true -> Collapsed, false -> Visible (reversed)</summary>
    public class InverseBoolToVis : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }
}
