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

        var worklist = new List<MoraAccount>();

        foreach (var account in accounts)
        {
            var row = Project(account, asOf, terms);
            if (row is not null)
            {
                worklist.Add(row);
            }
        }

        // Oldest debt first: that is the order somebody chasing payments works in.
        return worklist
            .OrderByDescending(row => row.DaysOverdue)
            .ThenBy(row => row.ContractId)
            .ToList();
    }

    private static MoraAccount? Project(ContractAccount account, DateOnly asOf, RecargoTerms terms)
    {
        // An account with no unpaid period is not in mora and does not appear. Note the
        // consequence, which follows the specification exactly: an opening balance carries no
        // period and therefore no due date, so it cannot by itself put an account on a list whose
        // first column is "days overdue". In practice such an account also has periods — the
        // opening balance is typed at go-live and the first period falls due that same month.
        var oldest = account.OldestUnpaidPeriod();
        if (oldest is null)
        {
            return null;
        }

        // The period's due date is its accrual's own date. Without an accrual there is nothing to
        // count days from, so there is no row to build.
        var accrual = account.Movements
            .FirstOrDefault(m => m.Kind == MovementKind.RentAccrual && m.Period == oldest.Value);

        if (accrual is null)
        {
            return null;
        }

        var daysOverdue = asOf.DayNumber - accrual.OccurredOn.DayNumber;
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

        return new MoraAccount(
            AccountId: account.Id,
            ContractId: account.ContractId,
            OldestUnpaidPeriod: oldest.Value,
            OldestUnpaidDueDate: accrual.OccurredOn,
            DaysOverdue: daysOverdue,
            LedgerBalance: ledger,
            AmountOwed: owed,
            RecargoAccrued: recargo,
            RecargoFrozenOn: frozen?.OccurredOn);
    }
}
