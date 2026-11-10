using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 18, Test 91, design §3.3: the difference a correction offers, computed as if the original
/// had carried the corrected canon. The worked example: 500,000, due July, confirmed at 575,000 applying from November.
/// </summary>
public class AdjustmentDifferenceCorrectionTests
{
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly November = new(2026, 11, 1);
    private static readonly DateOnly January = new(2027, 1, 1);

    private static (Contract Contract, ContractAccount Account, RentAdjustment Original) WorkedExample()
    {
        var contract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31), 500_000m, [new UnitShare(Guid.NewGuid(), 100m)], dueDay: 10);
        var original = ContractTestFactory.ConfirmAdjustment(
            contract, 500_000m, 15m, July, appliesFrom: November, confirmedAt: new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        return (contract, new ContractAccount(Guid.NewGuid(), contract.Id), original);
    }

    [Fact]
    public void Test91_TheCorrectedDifference_CountsBothStretches_330000()
    {
        var (contract, account, original) = WorkedExample();

        var offer = AdjustmentDifference.ForCorrection(contract, account, [original], January, 580_000m);

        Assert.Equal(80_000m * 4 + 5_000m * 2, offer!.Amount);
        Assert.Equal(330_000m, offer.Amount);
        Assert.Equal(
            [July, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), November, new DateOnly(2026, 12, 1)],
            offer.Months);
    }

    [Fact]
    public void ACorrectionOfACorrection_CountsEachStretchAgainstWhatWasInForce()
    {
        var (contract, account, original) = WorkedExample();
        var first = ContractTestFactory.ConfirmAdjustment(
            contract, 500_000m, 16m, July, appliesFrom: January,
            confirmedAt: new DateTimeOffset(2027, 1, 5, 12, 0, 0, TimeSpan.Zero), correctsAdjustmentId: original.Id);
        Assert.Equal(580_000m, first.NewCanon);

        var offer = AdjustmentDifference.ForCorrection(contract, account, [first, original], new DateOnly(2027, 3, 1), 590_000m);

        // July to October against 500,000, November and December against 575,000, January and February against 580,000.
        Assert.Equal(90_000m * 4 + 15_000m * 2 + 10_000m * 2, offer!.Amount);
    }

    [Fact]
    public void ACorrectionBelowWhatWasCharged_OffersNothing_WhenTheTotalIsNotPositive()
    {
        var (contract, account, original) = WorkedExample();

        Assert.Null(AdjustmentDifference.ForCorrection(contract, account, [original], January, 490_000m));
    }

    [Fact]
    public void Decision21_AppliesToTheTotal_NotToEachStretch()
    {
        // 560,000: July to October owe 60,000 each, November and December were overcharged by 15,000 each.
        var (contract, account, original) = WorkedExample();

        Assert.Equal(60_000m * 4 - 15_000m * 2, AdjustmentDifference.ForCorrection(contract, account, [original], January, 560_000m)!.Amount);
    }

    [Fact]
    public void TheSameFiguresAgain_OfferWhatTheOriginalWould_ForItsOwnStretchOnly()
    {
        // From the history, with no index changed: 575,000 again, applying from January. July to October still owe the rise;
        // November and December were already charged at 575,000 and owe nothing more.
        var (contract, account, original) = WorkedExample();

        Assert.Equal(75_000m * 4, AdjustmentDifference.ForCorrection(contract, account, [original], January, 575_000m)!.Amount);
    }

    [Fact]
    public void OpeningBalanceMonths_StillDoNotCount()
    {
        var (contract, account, original) = WorkedExample();
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 500_000m, new DateOnly(2026, 7, 1), July));

        Assert.Equal(80_000m * 3 + 5_000m * 2, AdjustmentDifference.ForCorrection(contract, account, [original], January, 580_000m)!.Amount);
    }
}
