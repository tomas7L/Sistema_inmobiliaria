using Inmobiliaria.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="IAccountMaterialiser"/>. Sits beside
/// <see cref="DueAdjustmentQuery"/>, which is where this project keeps its adapters.
/// </summary>
public sealed class AccountMaterialiser : IAccountMaterialiser
{
    /// <summary>Postgres's unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    private readonly InmobiliariaDbContext _context;

    public AccountMaterialiser(InmobiliariaDbContext context)
    {
        _context = context;
    }

    public async Task<int> MaterialiseDueAccrualsThroughAsync(
        Guid accountId, DateOnly through, CancellationToken ct = default)
    {
        // The account's lock comes first, as it does in CollectionDesk.CommitAsync, so the two writers
        // of this account's movements queue instead of interleaving their unique-index entries. EF orders
        // same-table INSERTs by key (random Guids), so two batches of different shape take
        // ux_account_movements_period_charge and ux_account_movements_instalment_period in opposite orders
        // and deadlock (40P01) under load. With the lock in place that cause is gone, so a 40P01 is NOT
        // treated as a lost race: it would hide a regression and silently skip due accruals. It surfaces.
        // A transaction the caller already opened is joined, not nested.
        if (_context.Database.CurrentTransaction is not null)
        {
            return await MaterialiseCoreAsync(accountId, through, ct);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        var written = await MaterialiseCoreAsync(accountId, through, ct);
        await transaction.CommitAsync(ct);

        return written;
    }

    private async Task<int> MaterialiseCoreAsync(Guid accountId, DateOnly through, CancellationToken ct)
    {
        await AccountRowLock.TakeAsync(_context, accountId, ct);

        var account = await _context.ContractAccounts
            .Include(a => a.Movements)
            .SingleOrDefaultAsync(a => a.Id == accountId, ct);

        if (account is null)
        {
            throw new InvalidOperationException($"No account exists with id '{accountId}'.");
        }

        // The adjustments come with it: a period accrues at the canon in force when it fell due,
        // which AccrualSchedule reconstructs from this history. Loading the contract without them
        // would silently price every past period at today's canon.
        var contract = await _context.Contracts
            .Include(c => c.Adjustments)
            .SingleAsync(c => c.Id == account.ContractId, ct);

        var written = 0;

        // AccrualSchedule already applies the cutover floor, so a pre-existing contract is never
        // asked for a period its opening balance carries (design D17). It is the only place that
        // rule lives, which is why this loop does not repeat it.
        foreach (var due in AccrualSchedule.PeriodsDue(contract, through))
        {
            // IsChargedFor, not HasAccrualFor: a period holding a monthly opening balance is charged
            // already, and the domain refuses a rent accrual on top of it (cobranza-y-recibos D18).
            // An early payment may also have brought the rent onto the account before its due date;
            // the row it wrote is identical to this one, so there is nothing left to write.
            if (account.IsChargedFor(due.Period))
            {
                continue;
            }

            account.Append(AccountMovement.RentAccrual(
                Guid.NewGuid(), due.Canon, due.Period, due.DueDate));

            written++;

            // The honorarios contractuales instalment rides with the rent of the k-th period
            // (design D19), dated on the same due date. HasInstalmentFor keeps it from being raised
            // twice; the unique index is the backstop for two writers at once.
            if (AccrualSchedule.InstalmentAmountFor(contract, due.Period) is { } fee &&
                !account.HasInstalmentFor(due.Period))
            {
                account.Append(AccountMovement.ContractualFeeInstalment(
                    Guid.NewGuid(), fee, due.Period, due.DueDate));

                written++;
            }
        }

        if (written == 0)
        {
            return 0;
        }

        // The write sits behind an explicit savepoint: a unique violation aborts the enclosing
        // transaction (25P02 on every later command), and the caller's transaction, or our own that is
        // about to commit, must stay usable after a lost race. EF's automatic savepoints do the same
        // by default, but this must not depend on that setting.
        var transaction = _context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Materialising requires an open transaction.");
        const string savepoint = "materialise_accruals";
        await transaction.CreateSavepointAsync(savepoint, ct);

        try
        {
            await _context.SaveChangesAsync(ct);
            await transaction.ReleaseSavepointAsync(savepoint, ct);
            return written;
        }
        catch (DbUpdateException ex) when (IsLostRace(ex))
        {
            await transaction.RollbackToSavepointAsync(savepoint, ct);

            // A competing writer materialised the same period between our read and our write —
            // the race design Decision 1 settles with a constraint rather than a lock.
            //
            // Re-read, do NOT retry the write. Retrying would be pointless and misleading: what
            // the other writer stored is identical to what we were about to store, because both
            // came from the same schedule over the same contract at the same canon. There is
            // nothing to merge and nothing to correct. Writing nothing is the right outcome.
            //
            // The tracker is cleared because our rejected movements are still sitting in the
            // loaded account's collection; leaving them attached would hand the next read an
            // account holding rows the database refused.
            _context.ChangeTracker.Clear();

            return 0;
        }
    }

    private static bool IsLostRace(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation };
}

/// <summary>
/// Serialises the writers of one account's movements. Both the lazy accrual and the collection desk take
/// it before reading or writing, so their INSERTs never interleave. A transaction-scoped advisory lock,
/// not <c>SELECT ... FOR UPDATE</c>: the application role has no UPDATE privilege on contract_accounts
/// (42501), and the lock needs none. It is released with the transaction, so callers must hold one.
/// </summary>
internal static class AccountRowLock
{
    public static async Task TakeAsync(InmobiliariaDbContext context, Guid accountId, CancellationToken ct)
    {
        var key = accountId.ToString();

        _ = await context.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS locked")
            .ToListAsync(ct);
    }
}
