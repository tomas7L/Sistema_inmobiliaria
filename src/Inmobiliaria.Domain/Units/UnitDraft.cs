namespace Inmobiliaria.Domain.Units;

/// <summary>A unit's address as the operator typed it, on the way to being created or corrected.</summary>
public sealed record UnitDraft(
    string? Street,
    string? Number,
    string? City,
    string? Province,
    string? PostalCode,
    string? Floor = null,
    string? Apartment = null)
{
    /// <summary>Everything trimmed and a blank floor or apartment made absent: what gets stored once checked.</summary>
    public UnitDraft Normalized() => new(
        Street?.Trim() ?? string.Empty,
        Number?.Trim() ?? string.Empty,
        City?.Trim() ?? string.Empty,
        Province?.Trim() ?? string.Empty,
        PostalCode?.Trim() ?? string.Empty,
        Absent(Floor),
        Absent(Apartment));

    private static string? Absent(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>The form's rule, pure and without a database. An identical address needs data, so the adapter finds that.</summary>
public static class UnitDraftRules
{
    /// <summary>The refusal <paramref name="draft"/> earns, or null when it can be written.</summary>
    public static UnitRefusal? Check(UnitDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var clean = draft.Normalized();

        return clean.Street!.Length == 0
            || clean.Number!.Length == 0
            || clean.City!.Length == 0
            || clean.Province!.Length == 0
            || clean.PostalCode!.Length == 0
                ? UnitRefusal.AddressIncomplete
                : null;
    }
}
