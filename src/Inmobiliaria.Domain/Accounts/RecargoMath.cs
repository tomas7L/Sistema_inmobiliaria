namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// The late-payment surcharge, as a pure function of what is owed, when it fell due, when the
/// question is asked, and the obligation's own terms.
///
/// Pure on purpose. It reads nothing, stores nothing and writes nothing: the passage of time alone
/// must never create a movement, because 2% per day on a contract three months late would otherwise
/// mean ninety ledger rows that say nothing (spec "Recargo Is Derived, Never Accrued as Daily
/// Movements"). It becomes a stored movement at exactly two instants — when a payment plan freezes
/// it, and when collection charges it on a receipt — and neither of those is here.
///
/// Nothing in this file knows that the rate is 2% or that the agency's leases fall due on the 10th.
/// Both arrive in <see cref="RecargoTerms"/>, and a test asserts no rate constant appears in this
/// source at all.
/// </summary>
public static class RecargoMath
{
    /// <summary>
    /// What <paramref name="amountOwed"/> has accrued in surcharge by <paramref name="asOf"/>.
    /// </summary>
    /// <param name="amountOwed">
    /// The outstanding amount the surcharge runs on. On a defaulted payment plan this is the
    /// balance still owing — not the missed instalment, and not the pre-plan debt — and it falls as
    /// the tenant resumes paying, so the daily figure falls with it.
    /// </param>
    /// <param name="dueDate">The day the obligation fell due; mora is counted from here.</param>
    /// <param name="asOf">The day the question is being asked.</param>
    /// <param name="terms">The obligation's own rate and grace. Never a constant.</param>
    /// <param name="frozenOn">
    /// The day a <em>novación</em> extinguished the obligation, when one has happened. Signing a
    /// payment plan replaces the debt rather than suspending it, so the surcharge stops on that
    /// date and <paramref name="asOf"/> is clamped to it. A balance asked for any earlier date
    /// still reads correctly, which is why this is a date and never a flag.
    /// </param>
    public static decimal For(
        decimal amountOwed,
        DateOnly dueDate,
        DateOnly asOf,
        RecargoTerms terms,
        DateOnly? frozenOn = null)
    {
        ArgumentNullException.ThrowIfNull(terms);

        if (amountOwed <= 0m)
        {
            return 0m;
        }

        // A novación extinguishes the obligation on its signing date. Asking about any later day
        // yields the same figure as asking on that day, because after it there was nothing left to
        // accrue against.
        var effectiveAsOf = frozenOn is not null && asOf > frozenOn.Value ? frozenOn.Value : asOf;

        var days = ChargeableDays(dueDate, effectiveAsOf, terms.GraceDays);
        if (days == 0)
        {
            return 0m;
        }

        // Simple interest, never compound, and never capped. Truncated once, here at the edge:
        // every intermediate value stays an untruncated decimal, matching how the canon is handled
        // in RentAdjustment.Confirm.
        return decimal.Truncate(amountOwed * terms.DailyRate * days);
    }

    /// <summary>
    /// Days of mora actually chargeable. Counted from the due date itself — the agency's real
    /// receipts show the surcharge as rent × 2% × (day paid − due day), so paying on the due day
    /// owes nothing and paying the next day owes one — then reduced by whatever grace the
    /// obligation grants.
    /// </summary>
    private static int ChargeableDays(DateOnly dueDate, DateOnly asOf, int graceDays)
    {
        var elapsed = asOf.DayNumber - dueDate.DayNumber;
        var chargeable = elapsed - graceDays;

        return chargeable > 0 ? chargeable : 0;
    }
}
