namespace Inmobiliaria.Domain.Parties;

/// <summary>
/// What a DNI and a CUIL must look like (gestion-de-alquileres spec Decision 28, design §2.1). Both are stored as
/// digits only, so dots and dashes cannot defeat uniqueness; empty means absent, stored as null.
/// </summary>
public static class PartyIdentifiers
{
    private const int DniMinimumDigits = 6;
    private const int DniMaximumDigits = 8;
    private const int CuilDigits = 11;

    private static readonly int[] CuilWeights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    /// <summary>
    /// Removes dots, dashes and spaces; nothing typed gives null. Any other character stays, so that "abc" is refused
    /// by <see cref="IsValidDni"/> rather than reading as absent.
    /// </summary>
    public static string? NormalizeDni(string? typed) => Strip(typed);

    /// <inheritdoc cref="NormalizeDni"/>
    public static string? NormalizeCuil(string? typed) => Strip(typed);

    /// <summary>Whether a normalized value is a DNI: 6 to 8 digits.</summary>
    public static bool IsValidDni(string? normalized) =>
        normalized is { Length: >= DniMinimumDigits and <= DniMaximumDigits } && normalized.All(char.IsAsciiDigit);

    /// <summary>
    /// Whether a normalized value is a CUIL: 11 digits whose last is the check digit. Eleven minus the weighted sum
    /// modulo eleven gives the digit; 11 means 0, and 10 has no valid digit.
    /// </summary>
    public static bool IsValidCuil(string? normalized)
    {
        if (normalized is not { Length: CuilDigits } || !normalized.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = CuilWeights.Select((weight, i) => weight * (normalized[i] - '0')).Sum();
        var digit = 11 - (sum % 11);

        return digit != 10 && (digit == 11 ? 0 : digit) == normalized[CuilDigits - 1] - '0';
    }

    private static string? Strip(string? typed)
    {
        if (typed is null)
        {
            return null;
        }

        var kept = new string(typed.Where(c => c is not ('.' or '-') && !char.IsWhiteSpace(c)).ToArray());

        return kept.Length == 0 ? null : kept;
    }
}
