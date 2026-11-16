using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// Shows an element while its bound value is set: true, a non-empty text, a count above zero, a non-empty list, or any other
/// value that is not null.
/// With the parameter "invert" it shows the element while the value is not set, as an "empty" note under a list.
/// </summary>
public sealed class SetToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var set = value switch
        {
            null => false,
            bool flag => flag,
            string text => text.Length > 0,
            int count => count > 0,
            ICollection list => list.Count > 0,
            _ => true,
        };

        var invert = string.Equals(parameter as string, "invert", StringComparison.Ordinal);
        return set != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Visibility is shown, never written back.");
}
