using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 15, Tests 45 and 48, design D1 and D2: a confirmed adjustment prices from its
/// applied-from period, not from its due date, whether or not the months before it are on the ledger yet (Finding F2).
/// </summary>
public class AdjustmentAppliesFromPricingTests
{
    private static readonly DateOnly July = new(2026, 7, 1);

    [Fact]
    public void Test45_ALateConfirmation_LeavesEveryUnwrittenMonthBeforeAppliesFromAtTheOldCanon()
    {
        // Canon 500,000, due 1 July, confirmed at 575,000 on 12 October: November is its applied-from period.
        var contract = ContractTestFactory.CreateActive(monthlyRent: 500_000m, dueDay: 10);
        ContractTestFactory.ConfirmAdjustment(
            contract, 500_000m, 15m, July,
            appliesFrom: new DateOnly(2026, 11, 1),
            confirmedAt: new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

        var periods = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 11, 30))
            .Where(p => p.Period >= new DateOnly(2026, 6, 1))
            .ToDictionary(p => p.Period, p => p.Canon);

        Assert.Equal(500_000m, periods[new DateOnly(2026, 6, 1)]);
        Assert.Equal(500_000m, periods[July]);
        Assert.Equal(500_000m, periods[new DateOnly(2026, 8, 1)]);
        Assert.Equal(500_000m, periods[new DateOnly(2026, 9, 1)]);
        Assert.Equal(500_000m, periods[new DateOnly(2026, 10, 1)]);
        Assert.Equal(575_000m, periods[new DateOnly(2026, 11, 1)]);
    }

    [Fact]
    public void Test45_ByCanonFor_TheMonthBeforeAppliesFromIsTheOldCanon_AndAppliesFromIsTheNew()
    {
        var contract = ContractTestFactory.CreateActive(monthlyRent: 500_000m, dueDay: 10);
        ContractTestFactory.ConfirmAdjustment(contract, 500_000m, 15m, July, appliesFrom: new DateOnly(2026, 11, 1));

        Assert.Equal(500_000m, AccrualSchedule.CanonFor(contract, July));
        Assert.Equal(500_000m, AccrualSchedule.CanonFor(contract, new DateOnly(2026, 10, 1)));
        Assert.Equal(575_000m, AccrualSchedule.CanonFor(contract, new DateOnly(2026, 11, 1)));
        Assert.Equal(575_000m, AccrualSchedule.CanonFor(contract, new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Test48_TwoAdjustmentsOnOneAppliedFromPeriod_TheLastConfirmedPricesIt()
    {
        var contract = ContractTestFactory.CreateActive(monthlyRent: 100_000m, dueDay: 10);
        var august = new DateOnly(2026, 8, 1);

        // Here list order and confirmation order agree; the reversed contract below appends the later confirmation first, so
        // only confirmation order can make both read the later one.
        var original = ContractTestFactory.ConfirmAdjustment(
            contract, 100_000m, 10m, July,
            appliesFrom: august,
            confirmedAt: new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
        ContractTestFactory.ConfirmAdjustment(
            contract, 100_000m, 20m, July,
            appliesFrom: august,
            confirmedAt: new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero),
            correctsAdjustmentId: original.Id);

        var reversed = ContractTestFactory.CreateActive(monthlyRent: 100_000m, dueDay: 10);
        var first = ContractTestFactory.ConfirmAdjustment(
            reversed, 100_000m, 20m, July,
            appliesFrom: august,
            confirmedAt: new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero));
        ContractTestFactory.ConfirmAdjustment(
            reversed, 100_000m, 10m, July,
            appliesFrom: august,
            confirmedAt: new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero),
            correctsAdjustmentId: first.Id);

        Assert.Equal(120_000m, AccrualSchedule.CanonFor(contract, august));
        Assert.Equal(100_000m, AccrualSchedule.CanonFor(contract, July));
        Assert.Equal(120_000m, AccrualSchedule.CanonFor(reversed, august));
    }

    [Fact]
    public void ARowWithoutAppliesFrom_StillPricesFromItsDueDate()
    {
        // Rows written before applies_from existed keep today's rule: in force from the due date of the period they fall in.
        var contract = ContractTestFactory.CreateActive(monthlyRent: 100_000m, dueDay: 10);
        ContractTestFactory.ConfirmAdjustment(contract, 100_000m, 10m, July);

        Assert.Equal(100_000m, AccrualSchedule.CanonFor(contract, new DateOnly(2026, 6, 1)));
        Assert.Equal(110_000m, AccrualSchedule.CanonFor(contract, July));
    }

    [Fact]
    public void BeforeAnyAdjustmentStarts_ThePeriodTakesTheEarliestRowsPreviousCanon()
    {
        var contract = ContractTestFactory.CreateActive(monthlyRent: 100_000m, dueDay: 10);
        ContractTestFactory.ConfirmAdjustment(contract, 100_000m, 10m, July, appliesFrom: new DateOnly(2026, 9, 1));
        ContractTestFactory.ConfirmAdjustment(contract, 110_000m, 10m, new DateOnly(2027, 1, 1), appliesFrom: new DateOnly(2027, 1, 1));

        Assert.Equal(100_000m, AccrualSchedule.CanonFor(contract, new DateOnly(2026, 8, 1)));
        Assert.Equal(110_000m, AccrualSchedule.CanonFor(contract, new DateOnly(2026, 12, 1)));
        Assert.Equal(121_000m, AccrualSchedule.CanonFor(contract, new DateOnly(2027, 1, 1)));
    }
}
