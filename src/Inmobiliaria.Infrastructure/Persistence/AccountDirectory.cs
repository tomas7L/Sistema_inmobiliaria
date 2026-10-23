using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="IAccountDirectory"/> (ui-de-cobranza design section 5.1). It reads
/// every contract account with its contract, parties and units, with no tracking, and never materialises
/// accruals (unlike <see cref="MoraWorklistQuery"/>): a search writes nothing. The text match is
/// <see cref="AccountSearch"/>, in memory. Names and units are formatted the way the recibo prints them.
/// </summary>
public sealed class AccountDirectory : IAccountDirectory
{
    private readonly InmobiliariaDbContext _context;
    private readonly IAgencyClock _clock;

    public AccountDirectory(InmobiliariaDbContext context, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        _context = context;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AccountListing>> SearchAsync(string text, CancellationToken ct = default)
    {
        if (!AccountSearch.IsSearchable(text))
        {
            return [];
        }

        var accounts = await _context.ContractAccounts.AsNoTracking()
            .Select(a => new { a.Id, a.ContractId })
            .ToListAsync(ct);
        var contracts = await _context.Contracts.AsNoTracking()
            .Include(c => c.Parties)
            .Include(c => c.Units)
            .ToDictionaryAsync(c => c.Id, ct);

        var partyIds = contracts.Values.SelectMany(c => c.Parties).Select(p => p.PartyId).Distinct().ToList();
        var unitIds = contracts.Values.SelectMany(c => c.Units).Select(u => u.UnitId).Distinct().ToList();
        var parties = await _context.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var units = await _context.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        // ContractAccount.IsFrozen's rule, asked of the database: a RecargoFrozen on or before today.
        var today = _clock.Today;
        var frozen = (await _context.ContractAccounts.AsNoTracking()
                .Where(a => a.Movements.Any(m => m.Kind == MovementKind.RecargoFrozen && m.OccurredOn <= today))
                .Select(a => a.Id)
                .ToListAsync(ct))
            .ToHashSet();

        // One row for the whole agency, so one query for every listing.
        var startingNumberSet = await _context.Database
            .SqlQuery<bool>($"""SELECT starting_number IS NOT NULL AS "Value" FROM recibo_numbering WHERE id = 1""")
            .SingleAsync(ct);

        string Names(Domain.Leasing.Contract contract, PartyRole role) => string.Join(
            ReciboRows.NameSeparator,
            contract.Parties
                .Where(p => p.Role == role)
                .Select(p => CollectionDesk.DescribeParty(parties[p.PartyId]))
                .Order(StringComparer.Ordinal));

        var listings = accounts.Select(a =>
        {
            var contract = contracts[a.ContractId];
            return new AccountListing(
                a.Id,
                contract.Id,
                contract.Id.ToString("N")[..8],
                Names(contract, PartyRole.Tenant),
                Names(contract, PartyRole.Lessor),
                string.Join(
                    ReciboRows.NameSeparator,
                    contract.Units.Select(u => CollectionDesk.DescribeUnit(units[u.UnitId])).Order(StringComparer.Ordinal)),
                contract.Status,
                contract.CutoverDate,
                // 0% is entered: the same test as the collection planner's.
                new AccountReadiness(contract.HonorariosPercentage is not null, frozen.Contains(a.Id), startingNumberSet));
        });

        return AccountSearch.Select(
            listings,
            text,
            l => [l.Tenants, l.Lessors, l.Units, l.ContractShortId],
            l => l.Tenants);
    }
}
