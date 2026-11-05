namespace Inmobiliaria.Domain.Parties;

/// <summary>A person as the operator typed them, on the way to being created or corrected.</summary>
public sealed record PartyDraft(
    string? FirstName,
    string? LastName,
    string? Dni = null,
    string? Cuil = null,
    string? Email = null,
    string? PhoneNumber = null,
    string? Domicile = null)
{
    /// <summary>Names trimmed, identifiers normalized and anything blank made absent: what gets stored once checked.</summary>
    public PartyDraft Normalized() => new(
        FirstName?.Trim() ?? string.Empty,
        LastName?.Trim() ?? string.Empty,
        PartyIdentifiers.NormalizeDni(Dni),
        PartyIdentifiers.NormalizeCuil(Cuil),
        Absent(Email),
        Absent(PhoneNumber),
        Absent(Domicile));

    private static string? Absent(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>The form's rules, pure and without a database (design D19). Duplicates need data, so the adapter finds those.</summary>
public static class PartyDraftRules
{
    /// <summary>The first refusal <paramref name="draft"/> earns, or null when it can be written.</summary>
    public static PartyRefusal? Check(PartyDraft draft, bool acceptNoIdentifier)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var normalized = draft.Normalized();

        if (normalized.FirstName!.Length == 0 || normalized.LastName!.Length == 0)
        {
            return PartyRefusal.NameMissing;
        }

        if (normalized.Dni is not null && !PartyIdentifiers.IsValidDni(normalized.Dni))
        {
            return PartyRefusal.DniInvalid;
        }

        if (normalized.Cuil is not null && !PartyIdentifiers.IsValidCuil(normalized.Cuil))
        {
            return PartyRefusal.CuilInvalid;
        }

        return normalized.Dni is null && normalized.Cuil is null && !acceptNoIdentifier
            ? PartyRefusal.NoIdentifierNotAccepted
            : null;
    }
}
