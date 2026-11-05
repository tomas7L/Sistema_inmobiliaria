namespace Inmobiliaria.Domain.Parties;

/// <summary>
/// What the Personas screen and the contract form do with persons (gestion-de-alquileres spec Requirements 1 to 3,
/// design §2.1): find them, create one and correct one. Anything an operator can cause comes back as a
/// <see cref="PartyRefusal"/>, never as an exception.
/// </summary>
public interface IPartyRegistry
{
    /// <summary>
    /// Everyone when <paramref name="text"/> is blank; otherwise the persons matching it by part of a name, ignoring
    /// case and accents, or by DNI or CUIL, ignoring dots and dashes.
    /// </summary>
    Task<IReadOnlyList<PartyListing>> SearchAsync(string? text, CancellationToken ct = default);

    /// <param name="acceptNoIdentifier">The operator accepted that a person with neither identifier cannot be checked for duplicates.</param>
    Task<PartyResult> CreateAsync(PartyDraft draft, bool acceptNoIdentifier, CancellationToken ct = default);

    /// <summary>Corrects a person in place. No history is kept, and issued recibos keep what they printed (spec Decision 29).</summary>
    /// <param name="acceptNoIdentifier">As in <see cref="CreateAsync"/>, since the checks of creating apply here too.</param>
    Task<PartyResult> CorrectAsync(Guid partyId, PartyDraft draft, bool acceptNoIdentifier, CancellationToken ct = default);
}

public sealed record PartyListing(
    Guid Id,
    string FirstName,
    string LastName,
    string? Dni,
    string? Cuil,
    string? Email,
    string? PhoneNumber,
    string? Domicile)
{
    public string FullName => $"{FirstName} {LastName}";

    /// <summary>A person with neither identifier shows a warning (spec Decision 28).</summary>
    public bool HasNoIdentifier => Dni is null && Cuil is null;

    public static PartyListing Of(Party party)
    {
        ArgumentNullException.ThrowIfNull(party);

        return new PartyListing(
            party.Id, party.FirstName, party.LastName, party.Dni, party.Cuil, party.Email, party.PhoneNumber, party.Domicile);
    }
}

public enum PartyRefusal
{
    NameMissing,
    DniInvalid,
    CuilInvalid,
    DniTaken,
    CuilTaken,

    /// <summary>Neither identifier was given and the operator has not accepted it yet.</summary>
    NoIdentifierNotAccepted,

    /// <summary>The person being corrected is no longer there.</summary>
    PartyNotFound,
}

/// <param name="Party">The person written, on success.</param>
/// <param name="Holder">Who already holds the identifier, on <see cref="PartyRefusal.DniTaken"/> or <see cref="PartyRefusal.CuilTaken"/>.</param>
public sealed record PartyResult(PartyRefusal? Refusal, PartyListing? Party, PartyListing? Holder)
{
    public bool Succeeded => Refusal is null;

    public static PartyResult Saved(PartyListing party) => new(null, party, null);

    public static PartyResult Refused(PartyRefusal refusal, PartyListing? holder = null) => new(refusal, null, holder);
}

public static class PartyRefusalText
{
    /// <param name="refusal">The refusal.</param>
    /// <param name="holderName">Who holds the identifier, named by the two "taken" refusals.</param>
    public static string OperatorText(this PartyRefusal refusal, string? holderName = null) => refusal switch
    {
        PartyRefusal.NameMissing =>
            "Escriba el nombre y el apellido de la persona.",
        PartyRefusal.DniInvalid =>
            "El DNI debe tener entre 6 y 8 números.",
        PartyRefusal.CuilInvalid =>
            "El CUIL debe tener 11 números y su último número no corresponde. Revíselo.",
        PartyRefusal.DniTaken =>
            $"Ese DNI ya lo tiene {holderName ?? "otra persona"}.",
        PartyRefusal.CuilTaken =>
            $"Ese CUIL ya lo tiene {holderName ?? "otra persona"}.",
        PartyRefusal.NoIdentifierNotAccepted =>
            "Sin DNI ni CUIL no se pueden detectar duplicados de esta persona. Confirme si quiere guardarla igual.",
        PartyRefusal.PartyNotFound =>
            "La persona ya no existe. Actualice la lista.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };
}
