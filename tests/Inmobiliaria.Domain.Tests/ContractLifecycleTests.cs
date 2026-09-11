using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

public class ContractLifecycleTests
{
    [Fact]
    public void Constructor_AlwaysCreatesActiveContract()
    {
        var contract = ContractTestFactory.CreateActive();

        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void PastNominalEndDate_WithNoNoticeGiven_RemainsActive()
    {
        var contract = new Contract(
            Guid.NewGuid(),
            new DateOnly(2020, 1, 1),
            new DateOnly(2021, 1, 1),
            monthlyRent: 100_000m);

        Assert.Equal(ContractStatus.Active, contract.Status);
        Assert.Null(contract.NoticeGivenDate);
        Assert.Null(contract.ActualEndDate);
    }

    [Fact]
    public void GiveNotice_TransitionsToPendingTermination()
    {
        var contract = ContractTestFactory.CreateActive();

        contract.GiveNotice(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1));

        Assert.Equal(ContractStatus.PendingTermination, contract.Status);
        Assert.Equal(new DateOnly(2026, 6, 1), contract.NoticeGivenDate);
        Assert.Equal(new DateOnly(2026, 7, 1), contract.PlannedMoveOutDate);
    }

    [Fact]
    public void End_WithoutReason_IsRejected()
    {
        var contract = ContractTestFactory.CreateActive();

        Assert.Throws<InvalidOperationException>(
            () => contract.End(reason: null, actualEndDate: new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void End_WithReason_TransitionsToEnded()
    {
        var contract = ContractTestFactory.CreateActive();

        contract.End(EndReason.Expiry, new DateOnly(2026, 12, 31));

        Assert.Equal(ContractStatus.Ended, contract.Status);
        Assert.Equal(EndReason.Expiry, contract.EndReason);
        Assert.Equal(new DateOnly(2026, 12, 31), contract.ActualEndDate);
    }
}
