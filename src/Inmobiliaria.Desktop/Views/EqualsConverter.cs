using System.Globalization;
using System.Windows.Data;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// Checks a radio button when the bound value equals its parameter, and writes the parameter back when it is checked.
/// A null parameter stands for "none", as the role filter's "Todas". Unchecking writes nothing: the checked one writes.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
