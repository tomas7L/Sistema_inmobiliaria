using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 19, Tests 62 and 63 on the account: the difference charge on the ledger, dated on its
/// period's due date, and a period holding nothing else once its contract ended.
/// </summary>
public class AdjustmentDifferenceLedgerTests
{
    private static readonly DateOnly November = new(2026, 11, 1);
    private static readonly DateOnly DueOn = new(2026, 11, 10);

    [Fact]
    public void TheCharge_IsPositive_OnAFirstOfMonth_AndDatedOnItsDueDate()
    {
        var charge = AccountMovement.AdjustmentDifference(Guid.NewGuid(), 300_000m, November, DueOn);

        Assert.Equal(MovementKind.AdjustmentDifference, charge.Kind);
        Assert.Equal(DueOn, charge.OccurredOn);
        Assert.Equal(November, charge.Period);
        Assert.Throws<ArgumentException>(() => AccountMovement.AdjustmentDifference(Guid.NewGuid(), 0m, November, DueOn));
        Assert.Throws<ArgumentException>(() => AccountMovement.AdjustmentDifference(Guid.NewGuid(), -1m, November, DueOn));
        Assert.Throws<ArgumentException>(() => AccountMovement.AdjustmentDifference(Guid.NewGuid(), 1m, DueOn, DueOn));
    }

    [Fact]
    public void Test62_TheBalance_IncludesTheCharge_FromItsDueDate()
    {
        var account = new ContractAccount(Guid.NewGuid(), Guid.NewGuid());
        account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 300_000m, November, DueOn));

        Assert.Equal(0m, account.LedgerBalance(DueOn.AddDays(-1)));
        Assert.Equal(300_000m, account.LedgerBalance(DueOn));
    }

    [Fact]
    public void TheCharge_DoesNotStopThePeriodAccruing()
    {
        var account = new ContractAccount(Guid.NewGuid(), Guid.NewGuid());
        account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 300_000m, November, DueOn));

        Assert.False(account.IsChargedFor(November));
        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 575_000m, November, DueOn));
        Assert.Equal(875_000m, account.PeriodNet(November));
    }

    [Fact]
    public void TwoCharges_OnOnePeriod_AreBothKept()
    {
        var account = new ContractAccount(Guid.NewGuid(), Guid.NewGuid());
        account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 160_000m, November, DueOn));
        account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 60_000m, November, DueOn));

        Assert.Equal(220_000m, account.DifferenceNet(November));
    }

    [Fact]
    public void Test63_APeriodItsEndedContractNeverAccrued_IsPayable_ForTheChargeAlone()
    {
        var contract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), 575_000m, [new UnitShare(Guid.NewGuid(), 100m)], dueDay: 10);
        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 10, 20));
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 300_000m, November, DueOn));

        Assert.DoesNotContain(AccrualSchedule.PeriodsBegun(contract, new DateOnly(2026, 11, 12)), p => p.Period == November);
        Assert.Equal(PeriodPayability.Payable, account.PayabilityOf(November, new DateOnly(2026, 11, 12), contract));
        Assert.Equal(PeriodPayability.NotBegun, account.PayabilityOf(November, new DateOnly(2026, 10, 31), contract));
    }
}
