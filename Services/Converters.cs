using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DeepLocal.Services
{
    /// <summary>true → Visible, false → Collapsed. Con Invert="True" il contrario.</summary>
    public sealed class BoolToVisibility : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Nega un bool (es. il pulsante che apre un popup non cliccabile mentre il popup è aperto).</summary>
    public sealed class InvertBool : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    }
}
