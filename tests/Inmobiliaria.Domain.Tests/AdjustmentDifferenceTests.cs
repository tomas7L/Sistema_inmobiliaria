using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 16, Tests 51 and 53 to 55, design §3.3: what a late adjustment would have charged on
/// the months already priced at the previous canon.
/// </summary>
public class AdjustmentDifferenceTests
{
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly November = new(2026, 11, 1);

    private static (Contract Contract, ContractAccount Account) WorkedExample(DateOnly? cutover = null)
    {
        var contract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31), 500_000m, [new UnitShare(Guid.NewGuid(), 100m)],
            dueDay: 10, cutoverDate: cutover);
        return (contract, new ContractAccount(Guid.NewGuid(), contract.Id));
    }

    // ---- Test 51 --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Test51_TheWorkedExample_Gives300000_OverJulyToOctober()
    {
        var (contract, account) = WorkedExample();

        var offer = AdjustmentDifference.Regular(contract, account, July, November, 500_000m, 575_000m);

        Assert.NotNull(offer);
        Assert.Equal(300_000m, offer.Amount);
        Assert.Equal([July, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], offer.Months);
    }

    [Fact]
    public void Test51_PaidAndUnpaidMonths_CountAlike()
    {
        var (contract, account) = WorkedExample();
        foreach (var month in new[] { July, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1) })
        {
            account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 500_000m, month, month.AddDays(9)));
        }

        account.Append(AccountMovement.Payment(Guid.NewGuid(), -500_000m, July, new DateOnly(2026, 7, 10)));
        account.Append(AccountMovement.Payment(Guid.NewGuid(), -500_000m, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10)));

        Assert.Equal(300_000m, AdjustmentDifference.Regular(contract, account, July, November, 500_000m, 575_000m)!.Amount);
        Assert.Equal(500_000m, account.PeriodNet(new DateOnly(2026, 9, 1)));
        Assert.Equal(500_000m, account.PeriodNet(new DateOnly(2026, 10, 1)));
    }

    // ---- Test 53 --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Test53_NotLate_OffersNothing()
    {
        var (contract, account) = WorkedExample();

        Assert.Null(AdjustmentDifference.Regular(contract, account, July, July, 500_000m, 575_000m));
    }

    [Fact]
    public void Test53_ALowerNewCanon_OffersNothing()
    {
        var (contract, account) = WorkedExample();

        Assert.Null(AdjustmentDifference.Regular(contract, account, July, November, 500_000m, 480_000m));
        Assert.Null(AdjustmentDifference.Regular(contract, account, July, November, 500_000m, 500_000m));
    }

    // ---- Test 54 --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Test54_OpeningBalanceMonths_AndMonthsOnOrBeforeTheCutover_DoNotCount()
    {
        // Cutover on 20 July: July fell due on the 10th, before it, so it belongs to the opening balance. August is carried by
        // an opening balance typed for it. September and October count.
        var (contract, account) = WorkedExample(cutover: new DateOnly(2026, 7, 20));
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 500_000m, new DateOnly(2026, 7, 20), new DateOnly(2026, 8, 1)));

        var offer = AdjustmentDifference.Regular(contract, account, July, November, 500_000m, 575_000m);

        Assert.Equal(150_000m, offer!.Amount);
        Assert.Equal([new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], offer.Months);
    }

    [Fact]
    public void MonthsAfterTheContractEnded_DoNotCount()
    {
        var (contract, account) = WorkedExample();
        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 9, 5));

        Assert.Equal(150_000m, AdjustmentDifference.Regular(contract, account, July, November, 500_000m, 575_000m)!.Amount);
    }

    // ---- Test 55 --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Test55_TwoLateSteps_EachCountsItsOwnMonthsAtItsOwnStep()
    {
        // 100,000 to 120,000 due January, and 120,000 to 150,000 due July, both confirmed in September and applying from it.
        var contract = new Contract(
            Guid.NewGuid(), new DateOnly(2025, 7, 1), new DateOnly(2028, 6, 30), 100_000m, [new UnitShare(Guid.NewGuid(), 100m)], dueDay: 10);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        var september = new DateOnly(2026, 9, 1);

        var first = AdjustmentDifference.Regular(contract, account, new DateOnly(2026, 1, 1), september, 100_000m, 120_000m);
        var second = AdjustmentDifference.Regular(contract, account, July, september, 120_000m, 150_000m);

        Assert.Equal(20_000m * 8, first!.Amount);
        Assert.Equal(30_000m * 2, second!.Amount);
    }
}
