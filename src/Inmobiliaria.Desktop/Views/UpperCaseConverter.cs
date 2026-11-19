using System.Globalization;
using System.Windows.Data;

namespace Inmobiliaria.Desktop.Views;

/// <summary>A text in capitals, as the canvas's sidebar headings ("COBRANZA"), whose CSS uppercases them.</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? text.ToUpper(culture) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A heading is shown, never written back.");
}
