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

    /// <summary>A month carried by an opening balance was charged whatever the agency typed, not at a canon this system knows.</summary>
    private static bool HasOpeningBalance(ContractAccount account, DateOnly period) =>
        account.Movements.Any(m => m.Kind == MovementKind.OpeningBalance && m.Period == period);
}
