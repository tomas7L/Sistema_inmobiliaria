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

        try
        {
            await _context.SaveChangesAsync(ct);
            return written;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
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

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation };
}
