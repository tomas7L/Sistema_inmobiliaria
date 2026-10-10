namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// The surcharge terms of one obligation: what rate it accrues at, and how many days it waits
/// before it starts.
///
/// These are arguments, never constants. A lease supplies 2% per day from its own due day; a
/// payment plan supplies whatever its two parties signed, because the agency owner sets those when
/// the plan is drawn up — he knows the person, and an aggressive rate on somebody who has already
/// proven they cannot pay recovers nothing. Same pattern the project already follows for the
/// adjustment clause, the honorarios percentage and the rent split: what the paper says, the record
/// carries (spec Decision 8).
///
/// There is deliberately NO default. A caller that forgets to supply terms must fail loudly rather
/// than silently inherit 2% from a constant nobody chose for it.
/// </summary>
public sealed record RecargoTerms
{
    /// <summary>
    /// Daily rate as a fraction: 0.02 is the two percent per day the agency's leases carry. Simple,
    /// never compound, and with no cap — the limit on an unpaid debt is legal rather than
    /// arithmetic, and a ceiling in code would compute something the two parties never signed.
    /// </summary>
    public decimal DailyRate { get; }

    /// <summary>
    /// Days after the due date before the surcharge begins. Zero for the agency's leases: the real
    /// receipts count from the due day itself, so paying on the due day owes nothing and paying the
    /// next day owes one day. A payment plan may carry more, if that is what was agreed.
    /// </summary>
    public int GraceDays { get; }

    public RecargoTerms(decimal dailyRate, int graceDays)
    {
        if (dailyRate < 0m)
        {
            throw new ArgumentException("A daily rate cannot be negative.", nameof(dailyRate));
        }

        // A rate above 100% per day is not a surcharge, it is a typo. Rejecting it here costs
        // nothing and stops a misplaced decimal point from reaching a tenant's account.
        if (dailyRate > 1m)
        {
            throw new ArgumentException(
                "A daily rate above 100% is almost certainly a misplaced decimal point.",
                nameof(dailyRate));
        }

        if (graceDays < 0)
        {
            throw new ArgumentException("Grace days cannot be negative.", nameof(graceDays));
        }

        DailyRate = dailyRate;
        GraceDays = graceDays;
    }
}
