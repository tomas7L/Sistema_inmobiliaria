using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>One account as the cash desk names it, with its contract's due day beside it.</summary>
/// <param name="Listing">The text the desk's search shows for the account.</param>
/// <param name="DueDay">The contract's due day, which the mora worklist needs to date an opening-balance month.</param>
/// <param name="LoadedByMistake">Whether the contract was cancelled as loaded by mistake; the desk never offers one.</param>
internal sealed record AccountListingEntry(AccountListing Listing, int DueDay, bool LoadedByMistake);

/// <summary>
/// How every account is named, read once for the cash desk's search and for the mora worklist (mora design D3), so the
/// two screens can never write a tenant's name differently. It reads every contract account with its contract, parties
/// and units, with no tracking, and writes nothing. Names and units are formatted the way the recibo prints them.
/// </summary>
internal static class AccountListings
{
    public static async Task<IReadOnlyList<AccountListingEntry>> ReadAllAsync(
        InmobiliariaDbContext context, DateOnly today, CancellationToken ct)
    {
        var accounts = await context.ContractAccounts.AsNoTracking()
            .Select(a => new { a.Id, a.ContractId })
            .ToListAsync(ct);
        var contracts = await context.Contracts.AsNoTracking()
            .Include(c => c.Parties)
            .Include(c => c.Units)
            .ToDictionaryAsync(c => c.Id, ct);

        var partyIds = contracts.Values.SelectMany(c => c.Parties).Select(p => p.PartyId).Distinct().ToList();
        var unitIds = contracts.Values.SelectMany(c => c.Units).Select(u => u.UnitId).Distinct().ToList();
        var parties = await context.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var units = await context.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        // ContractAccount.IsFrozen's rule, asked of the database: a RecargoFrozen on or before today.
        var frozen = (await context.ContractAccounts.AsNoTracking()
                .Where(a => a.Movements.Any(m => m.Kind == MovementKind.RecargoFrozen && m.OccurredOn <= today))
                .Select(a => a.Id)
                .ToListAsync(ct))
            .ToHashSet();

        // One row for the whole agency, so one query for every listing.
        var startingNumberSet = await context.Database
            .SqlQuery<bool>($"""SELECT starting_number IS NOT NULL AS "Value" FROM recibo_numbering WHERE id = 1""")
            .SingleAsync(ct);

        string Names(Domain.Leasing.Contract contract, PartyRole role) => string.Join(
            ReciboRows.NameSeparator,
            contract.Parties
                .Where(p => p.Role == role)
                .Select(p => CollectionDesk.DescribeParty(parties[p.PartyId]))
                .Order(StringComparer.Ordinal));

        return accounts.Select(a =>
        {
            var contract = contracts[a.ContractId];
            var listing = new AccountListing(
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

            return new AccountListingEntry(listing, contract.DueDay, contract.EndReason == Domain.Leasing.EndReason.LoadedByMistake);
        }).ToList();
    }
}
