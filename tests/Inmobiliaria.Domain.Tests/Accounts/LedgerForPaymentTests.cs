using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// notificaciones design §2.2, D2: the ledger half of <see cref="ContractAccount.AmountOwedForPayment"/>, extracted so a notice
/// can name the recargo apart as the desk figure minus the ledger. One formula, two readers: these tests pin the ledger to its
/// figure for every movement kind, and the recargo to the sum the desk projects, frozen included.
/// </summary>
public class LedgerForPaymentTests
{
    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly August = new(2026, 8, 1);
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly ReceivedOn = new(2026, 8, 20);

    /// <summary>
    /// Every kind but the frozen recargo on one account: a monthly opening balance, two rents (July settled with its recargo,
    /// August unpaid), an instalment, a correction, a difference on a month already begun and one on a month not yet begun.
    /// </summary>
    private static ContractAccount EveryKindAccount()
    {
        var account = new ContractAccount(Guid.NewGuid(), Guid.NewGuid());
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 100_000m, new DateOnly(2026, 6, 15), June));

        var julyRent = AccountMovement.RentAccrual(Guid.NewGuid(), 500_000m, July, new DateOnly(2026, 7, 10));
        account.Append(julyRent);
        account.Append(AccountMovement.ContractualFeeInstalment(Guid.NewGuid(), 50_000m, July, new DateOnly(2026, 7, 10)));
        account.Append(AccountMovement.RecargoCharged(Guid.NewGuid(), 20_000m, July, new DateOnly(2026, 7, 12)));
        account.Append(AccountMovement.Payment(Guid.NewGuid(), -570_000m, July, new DateOnly(2026, 7, 12)));
        account.Append(AccountMovement.Correction(Guid.NewGuid(), -1_000m, new DateOnly(2026, 7, 13), julyRent.Id, July));

        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 520_000m, August, new DateOnly(2026, 8, 10)));
        account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 30_000m, August, new DateOnly(2026, 8, 10)));

        // Attached to September, a month not yet begun on the received date: not owed today (design F8 of the adjustments).
        account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 40_000m, September, new DateOnly(2026, 8, 18)));

        return account;
    }

    [Fact]
    public void TheLedger_IsEveryMovementExceptADifferenceOnAMonthNotYetBegun()
    {
        var account = EveryKindAccount();

        // 100,000 + (500,000 + 50,000 + 20,000 − 570,000 − 1,000) + (520,000 + 30,000); September's 40,000 left out.
        Assert.Equal(649_000m, account.LedgerForPayment(ReceivedOn));
    }

    [Fact]
    public void TheDeskFigure_IsTheLedgerPlusTheRecargoTheDeskProjects()
    {
        var account = EveryKindAccount();

        var recargo = account.AmountOwedForPayment(ReceivedOn, LeaseTerms) - account.LedgerForPayment(ReceivedOn);

        var projected = new[] { June, July, August, September }
            .Sum(period => account.RecargoProjectedForPayment(period, ReceivedOn, LeaseTerms));
        Assert.True(recargo > 0m);
        Assert.Equal(projected, recargo);
    }

    [Fact]
    public void AFrozenAccount_HasNoRecargoBeyondItsLedger_WhichHoldsTheFrozenRecargo()
    {
        var account = EveryKindAccount();
        account.Append(AccountMovement.RecargoFrozen(Guid.NewGuid(), 75_000m, new DateOnly(2026, 8, 15)));

        Assert.Equal(724_000m, account.LedgerForPayment(ReceivedOn));
        Assert.Equal(account.LedgerForPayment(ReceivedOn), account.AmountOwedForPayment(ReceivedOn, LeaseTerms));
    }
}
