using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// One account currently in mora, in the shape a reminder can consume without knowing anything
/// about this capability's internals (spec "Accounts in Mora Are Queryable, and the List Is
/// Reminder-Ready"). Enough for a reminder to say "38 days overdue, owing 500,000 plus 380,000 in
/// recargo" instead of firing a blind monthly alert.
///
/// The two figures are named apart here as they are everywhere else. <paramref name="AmountOwed"/>
/// ALREADY INCLUDES <paramref name="RecargoAccrued"/> — it is the ledger balance plus the
/// surcharge — so adding them together double-counts. They are both present because a reminder
/// has to name the surcharge separately for the figure to mean anything to the person reading it.
///
/// What these figures cover: rent, honorarios contractuales instalments, and recargo. Nothing
/// else. Tasa municipal, seguros and "Otros Conceptos" are typed when a receipt is closed and
/// never accrue, so they are absent by design — and whatever displays this owes the reader that
/// sentence (spec "The Balance Names What It Covers"). The Mora screen says it (mora spec
/// Requirement 6).
///
/// The row names who owes in the cash desk's own words (mora spec Requirement 4): a reminder needs
/// nothing else to name the debtor, and the desk's search and this list can never disagree on a
/// name, because both are built by the same code.
/// </summary>
/// <param name="ContractShortId">The first eight hex characters of the contract id, as the desk shows it.</param>
/// <param name="Tenants">The tenants' names, "Last, First", as the recibo prints them.</param>
/// <param name="Units">The units, as the recibo describes them.</param>
/// <param name="Status">The contract's status: an ended contract can still owe.</param>
/// <param name="OldestUnpaidDueDate">
/// That period's due date: its rent's or its difference's own date, or, for an opening-balance
/// month, the contract's due day in that month (mora design D1).
/// </param>
/// <param name="DaysOverdue">
/// Counted from the OLDEST unpaid period's due date, which is the one a reminder leads with. The
/// later periods each keep their own clock inside <paramref name="AmountOwed"/>.
/// </param>
/// <param name="RecargoFrozenOn">
/// The day a novación extinguished the obligation, when one has happened; null otherwise. A date
/// rather than a flag, so a balance asked for an earlier day still reads as it did then — and so
/// that nothing in this namespace carries this state as a boolean.
/// </param>
public sealed record MoraAccount(
    Guid AccountId,
    Guid ContractId,
    string ContractShortId,
    string Tenants,
    string Units,
    ContractStatus Status,
    DateOnly OldestUnpaidPeriod,
    DateOnly OldestUnpaidDueDate,
    int DaysOverdue,
    decimal LedgerBalance,
    decimal AmountOwed,
    decimal RecargoAccrued,
    DateOnly? RecargoFrozenOn);
