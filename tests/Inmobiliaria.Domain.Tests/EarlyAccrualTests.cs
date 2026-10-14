using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// The current month is paid before its due date (cobranza-y-recibos spec Test 68, rent part;
/// design D1, D2). The payment brings the period's rent onto the account as the identical row lazy
/// accrual would write on the due date, and the payment is dated on that same day, so nothing about
/// a period appears on the ledger before it was charged.
///
/// The planner that does this for real arrives in PR 4; these tests assemble the same steps from
/// the pieces PR 1 adds, so the rule is fixed before anything is built on it. The instalment half
/// of Test 68 is task 2.5.
/// </summary>
public class EarlyAccrualTests
{
    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly SeptemberDue = new(2026, 9, 10);
    private static readonly DateOnly ReceivedOn = new(2026, 9, 2);

    private static Contract NewContract() =>
        new(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(Guid.NewGuid(), 100m)]);

    /// <summary>What lazy accrual writes: every due period the account does not hold yet.</summary>
    private static int Materialise(ContractAccount account, Contract contract, DateOnly asOf)
    {
        var written = 0;

        foreach (var due in AccrualSchedule.PeriodsDue(contract, asOf))
        {
            if (account.IsChargedFor(due.Period))
            {
                continue;
            }

            account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), due.Canon, due.Period, due.DueDate));
            written++;
        }

        return written;
    }

    /// <summary>
    /// The early payment's plan for the rent: write the period's accrual when it is missing, then a
    /// payment dated by the ledger date rule. Returns the movements written.
    /// </summary>
    private static List<AccountMovement> PayEarly(
        ContractAccount account, Contract contract, DateOnly period, DateOnly receivedOn)
    {
        var begun = AccrualSchedule.PeriodsBegun(contract, receivedOn).Single(p => p.Period == period);
        var written = new List<AccountMovement>();

        if (!account.IsChargedFor(period))
        {
            written.Add(AccountMovement.RentAccrual(Guid.NewGuid(), begun.Canon, begun.Period, begun.DueDate));
            account.Append(written[^1]);
        }

        // Not late, so no recargo: received minus due is negative and the arithmetic already says zero.
        Assert.Equal(0m, RecargoMath.For(begun.Canon, begun.DueDate, receivedOn, LeaseTerms));

        var paymentDate = LedgerDating.PaymentDate(receivedOn, chargeDate: begun.DueDate);
        written.Add(AccountMovement.Payment(Guid.NewGuid(), -begun.Canon, period, paymentDate));
        account.Append(written[^1]);

        return written;
    }

    /// <summary>
    /// Spec test 68: settled with no recargo, the rent brought onto the account, and the payment
    /// dated on the due date, not on the day the money arrived.
    /// </summary>
    [Fact]
    public void AnEarlyPayment_WritesTheRentItself_AndDatesThePaymentOnTheDueDate()
    {
        var contract = NewContract();
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(account, contract, new DateOnly(2026, 8, 31));

        Assert.DoesNotContain(AccrualSchedule.PeriodsDue(contract, ReceivedOn), p => p.Period == September);
        Assert.Contains(AccrualSchedule.PeriodsBegun(contract, ReceivedOn), p => p.Period == September);

        var written = PayEarly(account, contract, September, ReceivedOn);

        Assert.Equal([MovementKind.RentAccrual, MovementKind.Payment], written.Select(m => m.Kind));
        var rent = written[0];
        var payment = written[1];
        Assert.Equal(SeptemberDue, rent.OccurredOn);
        Assert.Equal(SeptemberDue, payment.OccurredOn);
        Assert.NotEqual(ReceivedOn, payment.OccurredOn);
        Assert.True(account.HasLivePaymentFor(September));
        Assert.Equal(0m, account.PeriodNet(September, SeptemberDue));
    }

    /// <summary>
    /// Between the 2nd and the 10th September reads as neither owed nor paid, exactly how an unpaid
    /// September reads today until the 10th; from the 10th it reads as owed and paid, zero.
    /// </summary>
    [Fact]
    public void BeforeTheDueDate_NothingAboutThePeriodIsOnTheLedger_AndAfterItThePeriodReadsZero()
    {
        var contract = NewContract();
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(account, contract, new DateOnly(2026, 8, 31));
        var balanceBefore = account.LedgerBalance(ReceivedOn);

        PayEarly(account, contract, September, ReceivedOn);

        Assert.Equal(balanceBefore, account.LedgerBalance(ReceivedOn));
        Assert.Equal(0m, account.PeriodNet(September, ReceivedOn));
        Assert.Equal(0m, account.PeriodNet(September, SeptemberDue));
        Assert.Equal(
            account.LedgerBalance(new DateOnly(2026, 9, 1)),
            account.LedgerBalance(SeptemberDue));
    }

    /// <summary>
    /// A later materialisation raises nothing again: the early row answers yes to IsChargedFor, so
    /// the account has exactly one rent row for the period.
    /// </summary>
    [Fact]
    public void ALaterMaterialisation_RaisesNothingAgain()
    {
        var contract = NewContract();
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(account, contract, new DateOnly(2026, 8, 31));
        PayEarly(account, contract, September, ReceivedOn);

        var raised = Materialise(account, contract, new DateOnly(2026, 9, 11));

        Assert.Equal(0, raised);
        Assert.True(account.IsChargedFor(September));
        Assert.Single(account.Movements, m => m.Kind == MovementKind.RentAccrual && m.Period == September);
    }

    /// <summary>
    /// The row written early is the row lazy accrual would write: same kind, period, amount and
    /// date (design D1), which is what makes the two paths idempotent with nothing to reconcile.
    /// </summary>
    [Fact]
    public void TheEarlyRentRow_IsIdenticalToTheRowLazyAccrualWouldWrite()
    {
        var contract = NewContract();
        var early = new ContractAccount(Guid.NewGuid(), contract.Id);
        var lazy = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(early, contract, new DateOnly(2026, 8, 31));
        Materialise(lazy, contract, new DateOnly(2026, 8, 31));

        PayEarly(early, contract, September, ReceivedOn);
        Materialise(lazy, contract, new DateOnly(2026, 9, 11));

        var earlyRent = Assert.Single(early.Movements, m => m.Kind == MovementKind.RentAccrual && m.Period == September);
        var lazyRent = Assert.Single(lazy.Movements, m => m.Kind == MovementKind.RentAccrual && m.Period == September);
        Assert.Equal(lazyRent.Amount, earlyRent.Amount);
        Assert.Equal(lazyRent.OccurredOn, earlyRent.OccurredOn);
        Assert.Equal(lazyRent.Period, earlyRent.Period);
    }

    /// <summary>
    /// A voided early payment is reversed on the later of the voiding day and the movement's date
    /// (design D2), so the period reappears as owed on the due date and counts recargo from it.
    /// </summary>
    [Fact]
    public void AVoidedEarlyPayment_IsReversedOnTheDueDate_AndTheRecargoCountsFromThere()
    {
        var contract = NewContract();
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(account, contract, new DateOnly(2026, 8, 31));
        var written = PayEarly(account, contract, September, ReceivedOn);
        var payment = written[1];

        var voidedOn = new DateOnly(2026, 9, 3);
        var reversalDate = LedgerDating.CorrectionDate(voidedOn, payment.OccurredOn);
        account.Append(AccountMovement.Correction(
            Guid.NewGuid(), -payment.Amount, reversalDate, payment.Id, September));

        Assert.Equal(SeptemberDue, reversalDate);
        Assert.Equal(0m, account.PeriodNet(September, voidedOn));
        Assert.Equal(500_000m, account.PeriodNet(September, SeptemberDue));
        Assert.Equal(
            RecargoMath.For(500_000m, SeptemberDue, new DateOnly(2026, 9, 15), LeaseTerms),
            account.RecargoProjectedFor(September, new DateOnly(2026, 9, 15), LeaseTerms));
    }

    /// <summary>
    /// The ledger date rule for an ordinary late payment collapses to the plain answers: the
    /// received date, and the voiding day.
    /// </summary>
    [Fact]
    public void ForAnOrdinaryLatePayment_TheLedgerDatesAreTheReceivedDateAndTheVoidingDay()
    {
        var chargedOn = new DateOnly(2026, 7, 10);
        var receivedOn = new DateOnly(2026, 7, 25);
        var voidedOn = new DateOnly(2026, 7, 28);

        Assert.Equal(receivedOn, LedgerDating.PaymentDate(receivedOn, chargedOn));
        Assert.Equal(voidedOn, LedgerDating.CorrectionDate(voidedOn, receivedOn));
    }

    /// <summary>
    /// A back-dated payment of an opening-balance month typed after the money arrived is dated on
    /// the day the balance was typed, never before it was charged.
    /// </summary>
    [Fact]
    public void ABackDatedPaymentOfAnOpeningBalanceMonth_IsDatedOnTheDayItWasTyped()
    {
        var typedOn = new DateOnly(2026, 10, 5);
        var receivedOn = new DateOnly(2026, 10, 3);

        Assert.Equal(typedOn, LedgerDating.PaymentDate(receivedOn, chargeDate: typedOn));
    }
}
