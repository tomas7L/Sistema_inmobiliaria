using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 15, Tests 44 and 47, design §3.2 and §3.4: the applied-from period of a
/// confirmation, the calendar that ignores it, and what <see cref="Contract.ConfirmAdjustment"/> refuses.
/// </summary>
public class AdjustmentTimingTests
{
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly August = new(2026, 8, 1);

    private static (Contract Contract, ContractAccount Account) DueDayTen()
    {
        var contract = ContractTestFactory.CreateActive(dueDay: 10);
        return (contract, new ContractAccount(Guid.NewGuid(), contract.Id));
    }

    [Fact]
    public void Test44_ConfirmedBeforeTheDueDay_AppliesAtOnce_AndIsNotLate()
    {
        var (contract, account) = DueDayTen();

        var appliesFrom = AdjustmentTiming.AppliesFrom(contract, account, new DateOnly(2026, 7, 5));

        Assert.Equal(July, appliesFrom);
        Assert.False(AdjustmentTiming.IsLate(July, appliesFrom));
    }

    [Fact]
    public void Test44_ConfirmedOnTheDueDay_AppliesNextMonth()
    {
        var (contract, account) = DueDayTen();

        var appliesFrom = AdjustmentTiming.AppliesFrom(contract, account, new DateOnly(2026, 7, 10));

        Assert.Equal(August, appliesFrom);
        Assert.True(AdjustmentTiming.IsLate(July, appliesFrom));
    }

    [Fact]
    public void Test44_ConfirmedAfterTheDueDay_AppliesNextMonth()
    {
        var (contract, account) = DueDayTen();

        Assert.Equal(August, AdjustmentTiming.AppliesFrom(contract, account, new DateOnly(2026, 7, 12)));
    }

    [Fact]
    public void Test44_APeriodPaidEarly_PushesItForward()
    {
        // Paying a period early writes its accrual first (CollectionPlanner), so July is already charged on 5 July.
        var (contract, account) = DueDayTen();
        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 100_000m, July, new DateOnly(2026, 7, 10)));
        account.Append(AccountMovement.Payment(Guid.NewGuid(), -100_000m, July, new DateOnly(2026, 7, 3)));

        Assert.Equal(August, AdjustmentTiming.AppliesFrom(contract, account, new DateOnly(2026, 7, 5)));
    }

    [Fact]
    public void AnOpeningBalanceTypedForTheMonth_AlsoPushesItForward()
    {
        var (contract, account) = DueDayTen();
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 100_000m, new DateOnly(2026, 7, 1), July));

        Assert.Equal(August, AdjustmentTiming.AppliesFrom(contract, account, new DateOnly(2026, 7, 5)));
    }

    [Fact]
    public void Test47_TheCalendarDoesNotMove_TheNextDueDateComesFromTheDueDate()
    {
        // A 6-month clause due 1 July and confirmed on 12 October applies from November, yet the next one is due 1 January.
        var contract = ContractTestFactory.CreateActive(dueDay: 10);
        ContractTestFactory.ConfirmAdjustment(contract, 100_000m, 10m, July, appliesFrom: new DateOnly(2026, 11, 1));

        var last = contract.Adjustments.Max(a => a.EffectiveDate);

        Assert.Equal(new DateOnly(2027, 1, 1), AdjustmentSchedule.NextDueDate(contract.StartDate, 6, last));
    }

    [Fact]
    public void ConfirmAdjustment_RecordsItsAppliedFromPeriod()
    {
        var contract = ContractTestFactory.CreateActive();

        var adjustment = Confirm(contract, appliesFrom: August);

        Assert.Equal(August, adjustment.AppliesFrom);
        Assert.Equal(July, adjustment.EffectiveDate);
    }

    [Fact]
    public void ConfirmAdjustment_OnAnEndedContract_IsRefused_AndChangesNothing()
    {
        var contract = ContractTestFactory.CreateActive();
        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 6, 20));

        Assert.Throws<InvalidOperationException>(() => Confirm(contract, appliesFrom: August));
        Assert.Empty(contract.Adjustments);
        Assert.Equal(100_000m, contract.MonthlyRent);
    }

    [Fact]
    public void ConfirmAdjustment_AppliedFromNotAFirstOfMonth_IsRefused()
    {
        var contract = ContractTestFactory.CreateActive();

        Assert.Throws<ArgumentException>(() => Confirm(contract, appliesFrom: new DateOnly(2026, 8, 10)));
        Assert.Empty(contract.Adjustments);
    }

    [Fact]
    public void ConfirmAdjustment_AppliedFromBeforeTheAdjustmentsMonth_IsRefused()
    {
        var contract = ContractTestFactory.CreateActive();

        Assert.Throws<ArgumentException>(() => Confirm(contract, appliesFrom: new DateOnly(2026, 6, 1)));
        Assert.Empty(contract.Adjustments);
        Assert.Equal(100_000m, contract.MonthlyRent);
    }

    [Fact]
    public void ConfirmAdjustment_OnAContractUnderNotice_IsAllowed()
    {
        var contract = ContractTestFactory.CreateActive();
        contract.GiveNotice(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 30));

        Confirm(contract, appliesFrom: July);

        Assert.Single(contract.Adjustments);
    }

    private static RentAdjustment Confirm(Contract contract, DateOnly appliesFrom)
    {
        var indexId = Guid.NewGuid();
        var indexValue = new RentAdjustmentIndexValue(
            indexId, indexId, new IndexPeriod(2026, 1), 100m, new IndexPeriod(2026, 6), 110m, 10m);
        var proposal = new AdjustmentProposal(100_000m, [indexValue], CombinationRule.Single, 10m, July);

        return contract.ConfirmAdjustment(
            Guid.NewGuid(), proposal, DateTimeOffset.UtcNow, Guid.NewGuid(), appliesFrom: appliesFrom);
    }
}
