using System.Globalization;
using System.Text.RegularExpressions;

namespace Inmobiliaria.Presentation.Indices;

/// <summary>
/// Reads a typed index level in the agency's format, <c>1.915.878,76</c> (gestion-de-alquileres spec Requirement 12, design §6).
/// Strict and independent of the machine's culture: a dot is only ever thousands in threes, because reading
/// <c>1915878.76</c> or <c>12.5</c> any other way would change a level that prices every contract reading it.
/// </summary>
public static class IndexLevelParser
{
    private static readonly Regex AgencyLevel = new(@"^([0-9]{1,3}(\.[0-9]{3})+|[0-9]+)(,[0-9]+)?$", RegexOptions.CultureInvariant);

    /// <returns>False when the text is empty or is not a number in that format.</returns>
    public static bool TryParse(string? text, out decimal level)
    {
        level = 0m;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !AgencyLevel.IsMatch(trimmed))
        {
            return false;
        }

        var invariant = trimmed.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');

        return decimal.TryParse(invariant, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out level);
    }
}
