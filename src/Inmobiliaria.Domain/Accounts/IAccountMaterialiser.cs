namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// Writes the rent accruals a contract has come due for but nobody has written yet.
///
/// This exists because there is no scheduler (design Decision 1). A period is materialised the
/// first time anything asks about the account — reading a balance, querying the mora worklist,
/// collection asking what to charge — rather than by a job that wakes up monthly. The operation
/// is "write every period due through this date that is not already written", so it is idempotent
/// and self-healing: asked after a three-month silence it writes three periods.
///
/// It is a NAMED STEP on purpose, never a side effect hidden inside a getter. A balance that
/// silently wrote rows when read would make every read a write, and nobody looking at a call site
/// would know.
///
/// Lives in Domain — a plain interface with no EF Core, Npgsql or WPF reference — so the
/// collection and notifications changes can depend on it without reaching into this capability's
/// internals. The only implementation is the Infrastructure adapter of the same name.
/// </summary>
public interface IAccountMaterialiser
{
    /// <summary>
    /// Writes every accrual due on or before <paramref name="through"/> that the account does not
    /// already hold, each at the canon in force for its own period, and returns how many were
    /// written. Zero means the account was already up to date — or that a competing writer got
    /// there first, in which case the caller simply re-reads.
    /// </summary>
    Task<int> MaterialiseDueAccrualsThroughAsync(
        Guid accountId, DateOnly through, CancellationToken ct = default);
}
