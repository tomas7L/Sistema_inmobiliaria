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
/// never accrue, so they are absent by design — and whatever eventually displays this owes the
/// reader that sentence (spec "The Balance Names What It Covers"). There is no screen in this
/// change; the UI pass owes the label, and this type owes it unambiguous names.
/// </summary>
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
    DateOnly OldestUnpaidPeriod,
    DateOnly OldestUnpaidDueDate,
    int DaysOverdue,
    decimal LedgerBalance,
    decimal AmountOwed,
    decimal RecargoAccrued,
    DateOnly? RecargoFrozenOn);
