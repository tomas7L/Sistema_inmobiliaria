using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-alquileres spec Decision 39 and Requirement 10: the month an end before its due date leaves uncharged.</summary>
public class MonthLeftUnchargedTests
{
    [Theory]
    [InlineData(10, 2026, 11, 5, 2026, 11)]
    [InlineData(10, 2026, 11, 1, 2026, 11)]
    [InlineData(10, 2026, 11, 9, 2026, 11)]
    [InlineData(31, 2027, 2, 27, 2027, 2)]
    public void AnEndBeforeTheMonthsDueDate_NamesThatMonth(int dueDay, int year, int month, int day, int namedYear, int namedMonth) =>
        Assert.Equal(new DateOnly(namedYear, namedMonth, 1), ContractLifecycleRules.MonthLeftUncharged(dueDay, null, new DateOnly(year, month, day)));

    [Theory]
    [InlineData(10, 2026, 11, 10)]
    [InlineData(10, 2026, 11, 15)]
    [InlineData(31, 2027, 2, 28)]
    [InlineData(1, 2026, 11, 1)]
    public void AnEndOnOrAfterTheDueDate_LeavesNoMonthUncharged(int dueDay, int year, int month, int day) =>
        Assert.Null(ContractLifecycleRules.MonthLeftUncharged(dueDay, null, new DateOnly(year, month, day)));

    [Fact]
    public void ADueDateOnOrBeforeTheCutover_WasNeverTheSystemsToCharge() =>
        Assert.Null(ContractLifecycleRules.MonthLeftUncharged(10, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 5)));

    [Fact]
    public void ACutoverBeforeTheDueDate_StillLeavesTheMonthUncharged() =>
        Assert.Equal(new DateOnly(2026, 11, 1), ContractLifecycleRules.MonthLeftUncharged(10, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 5)));
}
