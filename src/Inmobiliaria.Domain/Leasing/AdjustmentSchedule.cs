namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Derives a contract's next adjustment due date (spec "Next Adjustment Date Is Derived").
/// Never a free-standing stored field: computed from the clause's interval and either the
/// contract's last confirmed adjustment's effective date, or its start date when none exists
/// yet. Rent is charged per whole month with no proration, so the result always falls on the
/// first day of a monthly period. A pre-existing contract with none yet starts its calendar after its cutover date
/// (gestion-de-alquileres spec Requirement 17), taking the canon typed on creation as the one in force.
/// </summary>
public static class AdjustmentSchedule
{
    public static DateOnly NextDueDate(
        DateOnly startDate, int intervalMonths, DateOnly? lastEffectiveDate, DateOnly? cutoverDate = null)
    {
        if (intervalMonths is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalMonths), intervalMonths, "Interval must be between 1 and 60 months.");
        }

        if (lastEffectiveDate is null && cutoverDate is { } cutover)
        {
            return FirstStepAfter(startDate, intervalMonths, cutover);
        }

        return FirstOfMonth((lastEffectiveDate ?? startDate).AddMonths(intervalMonths));
    }

    private static DateOnly FirstStepAfter(DateOnly startDate, int intervalMonths, DateOnly cutover)
    {
        var step = 1;
        while (FirstOfMonth(startDate.AddMonths(step * intervalMonths)) <= cutover)
        {
            step++;
        }

        return FirstOfMonth(startDate.AddMonths(step * intervalMonths));
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
