using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// Which period a payment can settle, and which one it settles by default (cobranza-y-recibos spec
/// Tests 3 to 7, 9 and 53). Replaces the living contract-account Tests 14 and 15, which forbade any
/// choice. The predicate is pure: it reports a neutral reason and writes nothing.
/// </summary>
public class PayabilityTests
{
    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    private static readonly DateOnly May = new(2026, 5, 1);
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly August = new(2026, 8, 1);
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);

    /// <summary>The day the tenant pays: July, August and September have all fallen due.</summary>
    private static readonly DateOnly EndOfSeptember = new(2026, 9, 30);

    private static Contract ContractStarting(DateOnly start, DateOnly? cutover = null) =>
        new(
            Guid.NewGuid(),
            start,
            new DateOnly(2027, 12, 31),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(Guid.NewGuid(), 100m)],
            cutoverDate: cutover);

    /// <summary>An account holding what lazy accrual would have written by <paramref name="asOf"/>.</summary>
    private static ContractAccount AccruedThrough(Contract contract, DateOnly asOf)
    {
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        foreach (var due in AccrualSchedule.PeriodsDue(contract, asOf))
        {
            account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), due.Canon, due.Period, due.DueDate));
        }

        return account;
    }

    /// <summary>
    /// A late payment as collection will write it: the recargo charged first, then a payment for the
    /// period's whole figure. Both dated on the received date, which is after the due date.
    /// </summary>
    private static void Pay(ContractAccount account, DateOnly period, DateOnly receivedOn)
    {
        var recargo = account.RecargoProjectedFor(period, receivedOn, LeaseTerms);
        var owing = account.PeriodNet(period, receivedOn);

        if (recargo > 0m)
        {
            account.Append(AccountMovement.RecargoCharged(Guid.NewGuid(), recargo, period, receivedOn));
        }

        account.Append(AccountMovement.Payment(Guid.NewGuid(), -(owing + recargo), period, receivedOn));
    }

    /// <summary>Spec test 3: naming September settles September and leaves July and August unpaid.</summary>
    [Fact]
    public void NamingSeptember_SettlesSeptemberOnly_WhileOlderMonthsAreOwed()
    {
        var contract = ContractStarting(July);
        var account = AccruedThrough(contract, EndOfSeptember);

        Assert.Equal(PeriodPayability.Payable, account.PayabilityOf(September, EndOfSeptember, contract));

        Pay(account, September, EndOfSeptember);

        Assert.Equal(0m, account.PeriodNet(September, EndOfSeptember));
        Assert.Equal(500_000m, account.PeriodNet(July, EndOfSeptember));
        Assert.Equal(500_000m, account.PeriodNet(August, EndOfSeptember));
        Assert.True(account.HasLivePaymentFor(September));
        Assert.False(account.HasLivePaymentFor(July));
    }

    /// <summary>Spec test 4: with no period named, the oldest unpaid one is settled.</summary>
    [Fact]
    public void WithNoPeriodNamed_TheOldestOwedPeriodIsTheDefault()
    {
        var contract = ContractStarting(July);
        var account = AccruedThrough(contract, EndOfSeptember);

        Assert.Equal(July, account.DefaultPeriodToPay(EndOfSeptember, contract));
    }

    /// <summary>Spec test 5: the periods left unpaid keep counting from their own due dates.</summary>
    [Fact]
    public void AfterSeptemberIsSettled_JulyAndAugustKeepTheirOwnClocks()
    {
        var contract = ContractStarting(July);
        var account = AccruedThrough(contract, EndOfSeptember);
        Pay(account, September, EndOfSeptember);

        var asOf = new DateOnly(2026, 10, 5);

        var july = account.RecargoProjectedFor(July, asOf, LeaseTerms);
        var august = account.RecargoProjectedFor(August, asOf, LeaseTerms);

        Assert.Equal(RecargoMath.For(500_000m, new DateOnly(2026, 7, 10), asOf, LeaseTerms), july);
        Assert.Equal(RecargoMath.For(500_000m, new DateOnly(2026, 8, 10), asOf, LeaseTerms), august);
        Assert.True(july > august && august > 0m);
        Assert.Equal(0m, account.RecargoProjectedFor(September, asOf, LeaseTerms));
    }

    /// <summary>Spec test 6, first part: a period a live payment already settles is not payable.</summary>
    [Fact]
    public void ASettledPeriod_IsNotPayable_AndNothingIsAppended()
    {
        var contract = ContractStarting(July);
        var account = AccruedThrough(contract, EndOfSeptember);
        Pay(account, September, EndOfSeptember);
        var before = account.Movements.Count;

        Assert.Equal(PeriodPayability.AlreadySettled, account.PayabilityOf(September, EndOfSeptember, contract));
        Assert.Equal(before, account.Movements.Count);
    }

    /// <summary>Spec test 6, second part: a month that has not begun cannot be paid.</summary>
    [Fact]
    public void AMonthThatHasNotBegun_IsNotPayable_AndNothingIsAppended()
    {
        var contract = ContractStarting(July);
        var account = AccruedThrough(contract, EndOfSeptember);
        var before = account.Movements.Count;

        Assert.Equal(PeriodPayability.NotBegun, account.PayabilityOf(October, EndOfSeptember, contract));
        Assert.Equal(before, account.Movements.Count);
    }

    /// <summary>
    /// Spec test 6, third part: a period the contract does not charge, whether before it started or
    /// not the first day of a month at all.
    /// </summary>
    [Fact]
    public void APeriodNotOnTheContract_IsNotPayable_AndNothingIsAppended()
    {
        var contract = ContractStarting(July);
        var account = AccruedThrough(contract, EndOfSeptember);
        var before = account.Movements.Count;

        Assert.Equal(PeriodPayability.NotOnContract, account.PayabilityOf(June, EndOfSeptember, contract));
        Assert.Equal(
            PeriodPayability.NotOnContract,
            account.PayabilityOf(new DateOnly(2026, 8, 15), EndOfSeptember, contract));
        Assert.Equal(before, account.Movements.Count);
    }

    /// <summary>
    /// Spec test 6 with Decision 43 and design D24: a calendar month inside the contract's life
    /// that the schedule never produces is not on the contract.
    /// </summary>
    [Fact]
    public void ACalendarMonthTheContractNeverCharges_IsNotPayable()
    {
        // Signed on 20 July: July's 10th due date came before the contract existed.
        var contract = ContractStarting(new DateOnly(2026, 7, 20));
        var account = AccruedThrough(contract, EndOfSeptember);

        Assert.Equal(PeriodPayability.NotOnContract, account.PayabilityOf(July, EndOfSeptember, contract));
        Assert.Equal(PeriodPayability.Payable, account.PayabilityOf(August, EndOfSeptember, contract));
    }

    /// <summary>Spec test 7: an account with a frozen recargo accepts no payment.</summary>
    [Fact]
    public void AnAccountWithAFrozenRecargo_IsNotPayable()
    {
        var contract = ContractStarting(July);
        var account = AccruedThrough(contract, EndOfSeptember);
        account.Append(AccountMovement.RecargoFrozen(Guid.NewGuid(), 100_000m, EndOfSeptember));

        Assert.True(account.IsFrozen(EndOfSeptember));
        Assert.Equal(PeriodPayability.AccountFrozen, account.PayabilityOf(July, EndOfSeptember, contract));
        Assert.Null(account.DefaultPeriodToPay(EndOfSeptember, contract));
    }

    /// <summary>
    /// Spec test 9: after a payment the period nets zero, and the amount owed read the next day
    /// projects no recargo for it, which is what stops a double recargo.
    /// </summary>
    [Fact]
    public void AfterAPayment_ThePeriodNetsZero_AndProjectsNoRecargoTheNextDay()
    {
        var contract = ContractStarting(September);
        var account = AccruedThrough(contract, EndOfSeptember);

        Pay(account, September, EndOfSeptember);

        var nextDay = new DateOnly(2026, 10, 1);
        Assert.Equal(0m, account.PeriodNet(September, nextDay));
        Assert.Equal(0m, account.RecargoProjectedFor(September, nextDay, LeaseTerms));
        Assert.Equal(0m, account.LedgerBalance(nextDay));
        Assert.Equal(0m, account.AmountOwed(nextDay, LeaseTerms));
    }

    /// <summary>
    /// Spec test 53: one month of an old debt is paid by name, leaving the others; by default the
    /// oldest is paid. The contract predates the system, so its cutover date sits after those months.
    /// </summary>
    [Fact]
    public void OneMonthOfAnOldDebt_IsPaidByName_AndTheOldestIsTheDefault()
    {
        var cutover = new DateOnly(2026, 10, 5);
        var contract = ContractStarting(new DateOnly(2024, 1, 1), cutover);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 150_000m, cutover, May));
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 160_000m, cutover, June));
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 170_000m, cutover, July));
        var asOf = new DateOnly(2026, 10, 6);

        Assert.Equal(May, account.DefaultPeriodToPay(asOf, contract));
        Assert.Equal(PeriodPayability.Payable, account.PayabilityOf(June, asOf, contract));

        Pay(account, June, asOf);

        Assert.Equal(0m, account.PeriodNet(June, asOf));
        Assert.Equal(150_000m, account.PeriodNet(May, asOf));
        Assert.Equal(170_000m, account.PeriodNet(July, asOf));
        Assert.Equal(PeriodPayability.AlreadySettled, account.PayabilityOf(June, asOf, contract));
        Assert.Equal(May, account.DefaultPeriodToPay(asOf, contract));
    }

    /// <summary>
    /// With nothing owed, the default is the month in course when it has begun and is not yet paid,
    /// before its due date (spec Decision 37).
    /// </summary>
    [Fact]
    public void WithNothingOwed_TheDefaultIsTheMonthInCourse_UntilItIsPaid()
    {
        var contract = ContractStarting(July);
        var asOf = new DateOnly(2026, 9, 5);
        var account = AccruedThrough(contract, asOf);
        Pay(account, July, asOf);
        Pay(account, August, asOf);

        Assert.Equal(September, account.DefaultPeriodToPay(asOf, contract));
        Assert.Equal(PeriodPayability.Payable, account.PayabilityOf(September, asOf, contract));

        // An early payment: the period's rent and its payment both dated on the due date (D1, D2).
        var dueOn = new DateOnly(2026, 9, 10);
        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 500_000m, September, dueOn));
        account.Append(AccountMovement.Payment(Guid.NewGuid(), -500_000m, September, dueOn));

        Assert.Equal(PeriodPayability.AlreadySettled, account.PayabilityOf(September, asOf, contract));
        Assert.Null(account.DefaultPeriodToPay(asOf, contract));
    }

    /// <summary>
    /// A payment voided before its due date leaves the period charged but unpaid and not yet owed.
    /// It is payable again, which is why payable never asks whether the period is charged (design
    /// section 1; the predicate half of Test 73).
    /// </summary>
    [Fact]
    public void AnEarlyPaymentThatWasVoided_LeavesThePeriodPayableAgain()
    {
        var contract = ContractStarting(July);
        var asOf = new DateOnly(2026, 9, 5);
        var account = AccruedThrough(contract, new DateOnly(2026, 8, 31));
        Pay(account, July, asOf);
        Pay(account, August, asOf);

        var dueOn = new DateOnly(2026, 9, 10);
        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 500_000m, September, dueOn));
        var payment = AccountMovement.Payment(Guid.NewGuid(), -500_000m, September, dueOn);
        account.Append(payment);
        account.Append(AccountMovement.Correction(Guid.NewGuid(), 500_000m, dueOn, payment.Id, September));

        Assert.False(account.HasLivePaymentFor(September));
        Assert.True(account.IsChargedFor(September));
        Assert.Equal(PeriodPayability.Payable, account.PayabilityOf(September, asOf, contract));
    }
}
