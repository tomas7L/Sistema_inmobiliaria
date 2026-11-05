using Inmobiliaria.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// <see cref="IPartyRegistry"/> over <c>parties</c> (gestion-de-alquileres spec Requirements 1 to 3, design §2.1).
/// A duplicate is not looked for beforehand: the unique indexes refuse it, also between two operators at once, and
/// the holder is looked up after. Search is done in memory by <see cref="AccountSearch"/>'s rule (design D20).
/// </summary>
public sealed class PartyRegistry : IPartyRegistry
{
    private readonly InmobiliariaDbContext _context;

    public PartyRegistry(InmobiliariaDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async Task<IReadOnlyList<PartyListing>> SearchAsync(string? text, CancellationToken ct = default)
    {
        var persons = await _context.Parties.AsNoTracking().ToListAsync(ct);

        var found = string.IsNullOrWhiteSpace(text)
            ? persons.OrderBy(p => AccountSearch.Fold(SortKey(p)), StringComparer.Ordinal).ThenBy(TieBreak, StringComparer.Ordinal).ToList()
            : AccountSearch.Select(persons, SearchTerm(text), SearchFields, SortKey, TieBreak);

        return found.Select(PartyListing.Of).ToList();
    }

    public async Task<PartyResult> CreateAsync(PartyDraft draft, bool acceptNoIdentifier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (PartyDraftRules.Check(draft, acceptNoIdentifier) is { } refusal)
        {
            return PartyResult.Refused(refusal);
        }

        var clean = draft.Normalized();
        var party = new Party(
            Guid.NewGuid(), clean.FirstName!, clean.LastName!, clean.Dni, clean.Cuil, clean.Email, clean.PhoneNumber, clean.Domicile);
        _context.Parties.Add(party);

        return await SaveAsync(party, clean, ct);
    }

    public async Task<PartyResult> CorrectAsync(Guid partyId, PartyDraft draft, bool acceptNoIdentifier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (PartyDraftRules.Check(draft, acceptNoIdentifier) is { } refusal)
        {
            return PartyResult.Refused(refusal);
        }

        var party = await _context.Parties.SingleOrDefaultAsync(p => p.Id == partyId, ct);
        if (party is null)
        {
            return PartyResult.Refused(PartyRefusal.PartyNotFound);
        }

        var clean = draft.Normalized();
        party.CorrectIdentity(clean.FirstName!, clean.LastName!, clean.Dni, clean.Cuil);
        party.UpdateContactInfo(clean.Email, clean.PhoneNumber, clean.Domicile);

        return await SaveAsync(party, clean, ct);
    }

    private async Task<PartyResult> SaveAsync(Party party, PartyDraft clean, CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (RefusalOf(ex) is { } refusal)
        {
            _context.ChangeTracker.Clear();
            return PartyResult.Refused(refusal, await HolderAsync(refusal, clean, ct));
        }

        return PartyResult.Saved(PartyListing.Of(party));
    }

    private static PartyRefusal? RefusalOf(DbUpdateException ex) =>
        ex.InnerException is PostgresException database
            ? PartyRegistryErrorMap.For(database.SqlState, database.ConstraintName)
            : null;

    private async Task<PartyListing?> HolderAsync(PartyRefusal refusal, PartyDraft clean, CancellationToken ct)
    {
        var holder = refusal == PartyRefusal.DniTaken
            ? await _context.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Dni == clean.Dni, ct)
            : await _context.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Cuil == clean.Cuil, ct);

        return holder is null ? null : PartyListing.Of(holder);
    }

    /// <summary>
    /// Text with a digit and no letter is an identifier as someone may have written it, so only its digits are searched:
    /// "30.123.456" finds 30123456 (spec Requirement 1). Anything else is searched as a name.
    /// </summary>
    private static string SearchTerm(string text) =>
        text.Any(char.IsDigit) && !text.Any(char.IsLetter) ? PartyIdentifiers.NormalizeDni(text)! : text;

    /// <summary>Identifiers are normalized here too, since rows from before this rule may still hold dots.</summary>
    private static IEnumerable<string> SearchFields(Party party) =>
    [
        $"{party.FirstName} {party.LastName}",
        $"{party.LastName} {party.FirstName}",
        $"{party.LastName}, {party.FirstName}",
        PartyIdentifiers.NormalizeDni(party.Dni) ?? string.Empty,
        PartyIdentifiers.NormalizeCuil(party.Cuil) ?? string.Empty,
    ];

    private static string SortKey(Party party) => $"{party.LastName} {party.FirstName}";

    private static string TieBreak(Party party) => party.Id.ToString();
}

/// <summary>
/// The database's refusals of a person, keyed by SQLSTATE and constraint name and never by message text, like
/// <see cref="DeskErrorMap"/>. A pair not listed is null: it surfaces as an error instead of hiding a bug as an
/// operator message.
/// </summary>
public static class PartyRegistryErrorMap
{
    public static PartyRefusal? For(string? sqlState, string? constraintName) =>
        (sqlState, constraintName) switch
        {
            ("23505", "ix_parties_dni") => PartyRefusal.DniTaken,
            ("23505", "ix_parties_cuil") => PartyRefusal.CuilTaken,
            _ => null,
        };
}
