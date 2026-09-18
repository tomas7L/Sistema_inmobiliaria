namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Derives a contract's next adjustment due date (spec "Next Adjustment Date Is Derived").
/// Never a free-standing stored field: computed from the clause's interval and either the
/// contract's last confirmed adjustment's effective date, or its start date when none exists
/// yet. Rent is charged per whole month with no proration, so the result always falls on the
/// first day of a monthly period.
/// </summary>
public static class AdjustmentSchedule
{
    public static DateOnly NextDueDate(DateOnly startDate, int intervalMonths, DateOnly? lastEffectiveDate)
    {
        if (intervalMonths is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalMonths), intervalMonths, "Interval must be between 1 and 60 months.");
        }

        var baseline = lastEffectiveDate ?? startDate;
        var dueDate = baseline.AddMonths(intervalMonths);

        return new DateOnly(dueDate.Year, dueDate.Month, 1);
    }
}
