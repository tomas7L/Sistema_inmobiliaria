using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

public class AdjustmentScheduleTests
{
    [Fact]
    public void NextDueDate_NoConfirmedAdjustmentYet_UsesStartDatePlusInterval()
    {
        // Scenario "First due date before any adjustment": 2026-01-15 + 6 months, rounded to
        // the first of the month.
        var dueDate = AdjustmentSchedule.NextDueDate(new DateOnly(2026, 1, 15), intervalMonths: 6, lastEffectiveDate: null);

        Assert.Equal(new DateOnly(2026, 7, 1), dueDate);
    }

    [Fact]
    public void NextDueDate_AfterAConfirmedAdjustment_UsesItsEffectiveDatePlusInterval()
    {
        // Scenario "Due date after a confirmed adjustment": independent of when confirmation
        // itself was recorded.
        var dueDate = AdjustmentSchedule.NextDueDate(
            new DateOnly(2024, 1, 1), intervalMonths: 6, lastEffectiveDate: new DateOnly(2026, 7, 1));

        Assert.Equal(new DateOnly(2027, 1, 1), dueDate);
    }

    [Fact]
    public void NextDueDate_ThreeMonthInterval_IsNeverAssumedSemiannual()
    {
        // Spec test 18: a contract's own interval is honoured, never a hardcoded 6 months.
        var dueDate = AdjustmentSchedule.NextDueDate(new DateOnly(2026, 1, 1), intervalMonths: 3, lastEffectiveDate: null);

        Assert.Equal(new DateOnly(2026, 4, 1), dueDate);
        Assert.NotEqual(new DateOnly(2026, 7, 1), dueDate);
    }
}
