using System.Globalization;
using System.Text.RegularExpressions;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Presentation.Common;

/// <summary>
/// How every screen writes and reads money and dates, in the agency's convention (es-AR: "598.260",
/// "05/10/2026"). One place, so a figure reads the same on every screen and on the summary.
/// </summary>
public static class Formats
{
    public static readonly CultureInfo Agency = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>A signed amount, whole pesos when it has no cents.</summary>
    public static string Money(decimal amount) => amount.ToString("#,##0.##", Agency);

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>A month: "10/2026".</summary>
    public static string Period(DateOnly period) => period.ToString("MM/yyyy", CultureInfo.InvariantCulture);

    public static string Status(ContractStatus status) => status switch
    {
        ContractStatus.Active => "Vigente",
        ContractStatus.PendingTermination => "En rescisión",
        ContractStatus.Ended => "Finalizado",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A status without a name."),
    };

    // Thousands grouped by dots in threes, or no grouping at all; a comma for the cents. A dot anywhere else
    // ("1250.80", "12.5") is refused rather than read as grouping, which would multiply the amount (S3b review).
    private static readonly Regex AgencyAmount = new(@"^-?(\d{1,3}(\.\d{3})+|\d+)(,\d+)?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads an amount the operator typed, in the agency's convention ("598.260", "1250,80"). Null when the
    /// field is empty; false when it is not a number, or when a dot could be a decimal mark.
    /// </summary>
    public static bool TryParseMoney(string? text, out decimal? amount)
    {
        amount = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim();
        if (AgencyAmount.IsMatch(trimmed) && decimal.TryParse(trimmed, NumberStyles.Number, Agency, out var parsed))
        {
            amount = parsed;
            return true;
        }

        return false;
    }
}
