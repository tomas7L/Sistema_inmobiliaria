using Inmobiliaria.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="IMoraWorklistQuery"/>. Sits beside
/// <see cref="DueAdjustmentQuery"/> and <see cref="AccountMaterialiser"/>, which is where this
/// project keeps its adapters.
/// </summary>
public sealed class MoraWorklistQuery : IMoraWorklistQuery
{
    private readonly InmobiliariaDbContext _context;
    private readonly IAccountMaterialiser _materialiser;

    public MoraWorklistQuery(InmobiliariaDbContext context, IAccountMaterialiser materialiser)
    {
        _context = context;
        _materialiser = materialiser;
    }

    public async Task<IReadOnlyList<MoraAccount>> GetAccountsInMoraAsync(
        DateOnly asOf, RecargoTerms terms, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(terms);

        // MATERIALISE FIRST, AND FOR EVERY ACCOUNT — including the ones with no movements at all.
        //
        // This is the whole point. An account nobody has opened holds no accrual rows, so reading
        // movements alone would report it as clear, and the tenant three months behind would be
        // precisely the one who never shows up. The ids are collected before the loop because the
        // materialiser clears its change tracker when it loses a race, which would otherwise
        // invalidate an open query.
        //
        // Cost, stated rather than discovered later: this is a few round trips per account, so the
        // query is linear in accounts rather than one statement. At this agency's scale — around a
        // hundred contracts — that is a second or two, and correctness is why it is shaped this
        // way. If it ever needs to be cheaper, the fix is to batch the materialisation, never to
        // stop materialising.
        var accountIds = await _context.ContractAccounts
            .AsNoTracking()
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (var accountId in accountIds)
        {
            await _materialiser.MaterialiseDueAccrualsThroughAsync(accountId, asOf, ct);
        }

        // Read only now, and from scratch: anything the loop wrote is on disk, and anything it
        // detached would otherwise be missing from a reused tracked graph.
        var accounts = await _context.ContractAccounts
            .AsNoTracking()
            .Include(a => a.Movements)
            .ToListAsync(ct);

        // Who owes, in the cash desk's own words, and the due day an opening-balance month is dated by
        // (mora design D3). Read by the same code the desk's search uses, so the two never disagree.
        var listings = (await AccountListings.ReadAllAsync(_context, asOf, ct))
            .ToDictionary(e => e.Listing.AccountId);

        var worklist = new List<MoraAccount>();

        foreach (var account in accounts)
        {
            // Every account has a contract (a foreign key), so a missing entry is a bug, never a row to
            // skip: a skipped row is exactly the failure the worklist exists to prevent.
            var row = Project(account, listings[account.Id], asOf, terms);
            if (row is not null)
            {
                worklist.Add(row);
            }
        }

        // Oldest debt first: that is the order somebody chasing payments works in. Ties by tenant name,
        // folded as the desk's search folds it, then by account so the order never shuffles (mora D4).
        return worklist
            .OrderByDescending(row => row.DaysOverdue)
            .ThenBy(row => AccountSearch.Fold(row.Tenants), StringComparer.Ordinal)
            .ThenBy(row => row.AccountId)
            .ToList();
    }

    private static MoraAccount? Project(ContractAccount account, AccountListingEntry entry, DateOnly asOf, RecargoTerms terms)
    {
        // An account with no unpaid period is not in mora and does not appear. A zero opening balance
        // carries no period and so never makes a period unpaid; a monthly one carries its month.
        var oldest = account.OldestUnpaidPeriod();
        if (oldest is null)
        {
            return null;
        }

        var dueDate = DueDateOf(account, oldest.Value, entry.DueDay);

        var daysOverdue = asOf.DayNumber - dueDate.DayNumber;
        if (daysOverdue <= 0)
        {
            return null;
        }

        var ledger = account.LedgerBalance(asOf);
        var owed = account.AmountOwed(asOf, terms);

        // A frozen account reports the FROZEN figure, not a figure that kept growing — and not
        // zero. Once the novación's surcharge became a movement it joined the ledger balance, so
        // the difference between the two figures is no longer the surcharge: it is nothing. The
        // movement itself is the only place the real number still is.
        var frozen = account.Movements
            .Where(m => m.Kind == MovementKind.RecargoFrozen && m.OccurredOn <= asOf)
            .OrderBy(m => m.OccurredOn)
            .LastOrDefault();

        var recargo = frozen is not null ? frozen.Amount : owed - ledger;

        var listing = entry.Listing;
        return new MoraAccount(
            AccountId: account.Id,
            ContractId: account.ContractId,
            ContractShortId: listing.ContractShortId,
            Tenants: listing.Tenants,
            Units: listing.Units,
            Status: listing.Status,
            OldestUnpaidPeriod: oldest.Value,
            OldestUnpaidDueDate: dueDate,
            DaysOverdue: daysOverdue,
            LedgerBalance: ledger,
            AmountOwed: owed,
            RecargoAccrued: recargo,
            RecargoFrozenOn: frozen?.OccurredOn);
    }

    /// <summary>
    /// One rule for every unpaid period (mora design D1): its rent's own date, else its difference's
    /// own date, both already the period's due date; else the contract's due day in that month, by the
    /// rule rent itself uses. The last branch is what an opening-balance month takes: it carries no
    /// rent, and before this rule an account whose oldest unpaid period was one dropped off the list
    /// (mora spec Finding F1). Never the day the balance was typed.
    /// </summary>
    private static DateOnly DueDateOf(ContractAccount account, DateOnly period, int dueDay) =>
        account.Movements.FirstOrDefault(m => m.Kind == MovementKind.RentAccrual && m.Period == period)?.OccurredOn
        ?? account.Movements.FirstOrDefault(m => m.Kind == MovementKind.AdjustmentDifference && m.Period == period)?.OccurredOn
        ?? AccrualSchedule.DueDateFor(period, dueDay);
}
