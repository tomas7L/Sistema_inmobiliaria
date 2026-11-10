using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Leasing;

/// <param name="Amount">What the new canon would have charged over the old one on those months.</param>
/// <param name="Months">The months it covers, oldest first.</param>
public sealed record DifferenceOffer(decimal Amount, IReadOnlyList<DateOnly> Months);

/// <summary>
/// What a late adjustment's new canon would have charged on the months already priced at the old one (gestion-de-alquileres
/// spec Requirement 16 and Decision 17, design §3.3). It never re-prices those months: it is offered as one separate charge,
/// which the operator chooses to write or not.
/// </summary>
public static class AdjustmentDifference
{
    /// <summary>
    /// Counts each month from the adjustment's own month up to, not including, <paramref name="appliesFrom"/> that the schedule
    /// charges and no opening balance carries, whether or not it is on the ledger or paid, and multiplies by the step from
    /// <paramref name="previousCanon"/> to <paramref name="newCanon"/>. Comparing with the step's own previous canon, never
    /// with the amount charged, is what keeps two late steps from counting the same rise twice (spec Test 55). Null when not
    /// late, when the canon did not rise, or when no month counts.
    /// </summary>
    public static DifferenceOffer? Regular(
        Contract contract, ContractAccount account, DateOnly adjustmentMonth, DateOnly appliesFrom, decimal previousCanon, decimal newCanon)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(account);

        var step = newCanon - previousCanon;
        if (step <= 0m)
        {
            return null;
        }

        var months = new List<DateOnly>();
        for (var period = new DateOnly(adjustmentMonth.Year, adjustmentMonth.Month, 1); period < appliesFrom; period = period.AddMonths(1))
        {
            if (AccrualSchedule.IsChargedPeriod(contract, period) && !HasOpeningBalance(account, period))
            {
                months.Add(period);
            }
        }

        return months.Count == 0 ? null : new DifferenceOffer(step * months.Count, months);
    }

    /// <summary>
    /// The difference a correction offers, as if the original had carried <paramref name="correctedCanon"/> (spec Requirement 18,
    /// design §3.3). <paramref name="chain"/> is the corrected row back to its root, the original. Each month the schedule charges
    /// from the root's month up to, not including, the correction's <paramref name="appliesFrom"/> counts the corrected canon
    /// over what was in force for it: the new canon of the latest row in the chain that had started by then, else the root's
    /// previous canon. For one correction that is the spec's two stretches; it stays right for a correction of a correction.
    /// Null when the total is not positive (spec Decision 21).
    /// </summary>
    public static DifferenceOffer? ForCorrection(
        Contract contract, ContractAccount account, IReadOnlyList<RentAdjustment> chain, DateOnly appliesFrom, decimal correctedCanon)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
        {
            throw new ArgumentException("A correction corrects at least one adjustment.", nameof(chain));
        }

        var root = chain[^1];
        var byStart = chain
            .OrderBy(Start)
            .ThenBy(a => a.ConfirmedAt)
            .ToList();

        var months = new List<DateOnly>();
        var amount = 0m;
        for (var period = FirstOf(root.EffectiveDate); period < appliesFrom; period = period.AddMonths(1))
        {
            if (!AccrualSchedule.IsChargedPeriod(contract, period) || HasOpeningBalance(account, period))
            {
                continue;
            }

            var inForce = byStart.LastOrDefault(a => Start(a) <= period);
            amount += correctedCanon - (inForce?.NewCanon ?? root.PreviousCanon);
            months.Add(period);
        }

        return amount <= 0m ? null : new DifferenceOffer(amount, months);
    }

    /// <summary>Where a row starts pricing: its applied-from period, or the month it fell due in for a row from before that existed.</summary>
    private static DateOnly Start(RentAdjustment adjustment) => adjustment.AppliesFrom ?? FirstOf(adjustment.EffectiveDate);

    private static DateOnly FirstOf(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>A month carried by an opening balance was charged whatever the agency typed, not at a canon this system knows.</summary>
    private static bool HasOpeningBalance(ContractAccount account, DateOnly period) =>
        account.Movements.Any(m => m.Kind == MovementKind.OpeningBalance && m.Period == period);
}
