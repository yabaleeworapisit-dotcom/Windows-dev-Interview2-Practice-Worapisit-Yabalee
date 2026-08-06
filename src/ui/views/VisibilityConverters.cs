using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NasaApodApp.Views;

// Shows the bound element when the bound flag is true. WPF ships a converter that does this,
// but pairing it with the inverse below keeps both directions written the same way in XAML.
public sealed class TrueToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Visibility is displayed, never edited back into the view model.");
}

// Shows the bound element when the bound flag is false — used for the "no picture yet"
// placeholder that stands in while a download is still running.
public sealed class FalseToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Visibility is displayed, never edited back into the view model.");
}
