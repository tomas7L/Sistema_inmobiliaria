using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 16 and Test 52, design D5: the difference charge carries no recargo and does not raise
/// the recargo of the period it attaches to. November owes 575,000 of rent and a 300,000 charge, both due on the 10th, and is
/// read 10 days late.
/// </summary>
public class AdjustmentDifferenceRecargoTests
{
    private static readonly DateOnly November = new(2026, 11, 1);
    private static readonly DateOnly DueOn = new(2026, 11, 10);
    private static readonly DateOnly TenDaysLate = new(2026, 11, 20);
    private static readonly RecargoTerms Terms = new(0.02m, 0);

    private static ContractAccount WithCharge(bool withDifference = true)
    {
        var account = new ContractAccount(Guid.NewGuid(), Guid.NewGuid());
        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 575_000m, November, DueOn));
        if (withDifference)
        {
            account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 300_000m, November, DueOn));
        }

        return account;
    }

    private static decimal OnRentAlone => RecargoMath.For(575_000m, DueOn, TenDaysLate, Terms);

    [Fact]
    public void Test52_TheProjectedRecargo_IsOnTheRentAlone()
    {
        var account = WithCharge();

        Assert.True(OnRentAlone > 0m);
        Assert.Equal(OnRentAlone, account.RecargoProjectedFor(November, TenDaysLate, Terms));
        Assert.NotEqual(RecargoMath.For(875_000m, DueOn, TenDaysLate, Terms), account.RecargoProjectedFor(November, TenDaysLate, Terms));
    }

    [Fact]
    public void Test52_TheRecargoAPaymentCharges_IsOnTheRentAlone()
    {
        Assert.Equal(OnRentAlone, WithCharge().RecargoProjectedForPayment(November, TenDaysLate, Terms));
    }

    [Fact]
    public void Test52_TheAmountOwed_IsRentPlusChargePlusTheRecargoOnTheRent()
    {
        Assert.Equal(875_000m + OnRentAlone, WithCharge().AmountOwed(TenDaysLate, Terms));
        Assert.Equal(875_000m + OnRentAlone, WithCharge().AmountOwedForPayment(TenDaysLate, Terms));
    }

    [Fact]
    public void WithoutACharge_TheRecargoIsUnchanged()
    {
        Assert.Equal(OnRentAlone, WithCharge(withDifference: false).RecargoProjectedFor(November, TenDaysLate, Terms));
    }

    [Fact]
    public void DifferenceNet_SumsTheChargesOfThePeriod_AndTheCorrectionsNamingThem()
    {
        var account = WithCharge();
        var second = AccountMovement.AdjustmentDifference(Guid.NewGuid(), 60_000m, November, DueOn);
        account.Append(second);
        account.Append(AccountMovement.Correction(Guid.NewGuid(), -60_000m, DueOn, second.Id, November));

        Assert.Equal(300_000m, account.DifferenceNet(November));
        Assert.Equal(0m, account.DifferenceNet(new DateOnly(2026, 12, 1)));
        Assert.Equal(0m, account.DifferenceNet(November, DueOn.AddDays(-1)));
        Assert.Equal(300_000m, account.DifferenceNet(November, DueOn));
    }

    [Fact]
    public void ACorrectionOfTheRent_IsNotPartOfTheDifference()
    {
        var account = WithCharge();
        var accrual = account.Movements.Single(m => m.Kind == MovementKind.RentAccrual);
        account.Append(AccountMovement.Correction(Guid.NewGuid(), -75_000m, DueOn, accrual.Id, November));

        Assert.Equal(300_000m, account.DifferenceNet(November));
        Assert.Equal(RecargoMath.For(500_000m, DueOn, TenDaysLate, Terms), account.RecargoProjectedFor(November, TenDaysLate, Terms));
    }

    /// <summary>
    /// Design F8: the desk's "previous amount owed" sums every movement whatever its date, so a charge attached to a month not yet
    /// begun would read as owed early. It counts from the 1st of its month, when the period can be paid.
    /// </summary>
    [Fact]
    public void TheAmountOwedForAPayment_LeavesOutAChargeWhoseMonthHasNotBegun()
    {
        var october = new DateOnly(2026, 10, 1);
        var account = new ContractAccount(Guid.NewGuid(), Guid.NewGuid());
        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 575_000m, october, new DateOnly(2026, 10, 10)));
        var charge = AccountMovement.AdjustmentDifference(Guid.NewGuid(), 300_000m, November, DueOn);
        account.Append(charge);
        account.Append(AccountMovement.Correction(Guid.NewGuid(), -100_000m, DueOn, charge.Id, November));
        var noGrace = new RecargoTerms(0m, 0);

        Assert.Equal(575_000m, account.AmountOwedForPayment(new DateOnly(2026, 10, 31), noGrace));
        Assert.Equal(775_000m, account.AmountOwedForPayment(November, noGrace));
    }
}
