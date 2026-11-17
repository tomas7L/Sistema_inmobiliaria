using System.Collections;
using System.Globalization;
using System.Windows.Data;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Contracts;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The error under one row of a list field of the contract form (lessors, codeudores, units), found by the row's position in
/// its list. Bound to the form's errors, the row and the list, in that order; the field is named by a fourth value, or else
/// by the parameter.
/// </summary>
public sealed class RowErrorConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = values.Length > 3 ? values[3] as string : parameter as string;
        if (values is [ContractFieldErrors errors, var row, IList rows, ..]
            && Enum.TryParse<ContractField>(name, out var field)
            && rows.IndexOf(row) is >= 0 and var position)
        {
            return errors.ForRow(field, position);
        }

        return null;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
