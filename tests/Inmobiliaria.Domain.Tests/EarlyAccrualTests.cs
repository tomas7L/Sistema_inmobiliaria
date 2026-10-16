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
/// of Test 68 (task 2.5) lives in the same file: no planner class exists yet, so the same helper
/// assembles the instalment step from AccrualSchedule.InstalmentAmountFor and HasInstalmentFor.
/// </summary>
public class EarlyAccrualTests
{
    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly SeptemberDue = new(2026, 9, 10);
    private static readonly DateOnly ReceivedOn = new(2026, 9, 2);

    private static Contract NewContract(ContractualFeePlan? plan = null) =>
        new(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(Guid.NewGuid(), 100m)],
            contractualFeePlan: plan);

    /// <summary>What lazy accrual writes: every due period the account does not hold yet, with its instalment.</summary>
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

            if (AccrualSchedule.InstalmentAmountFor(contract, due.Period) is { } fee &&
                !account.HasInstalmentFor(due.Period))
            {
                account.Append(AccountMovement.ContractualFeeInstalment(
                    Guid.NewGuid(), fee, due.Period, due.DueDate));
                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// The early payment's plan: write the rent of the period when <see cref="ContractAccount.IsChargedFor"/>
    /// says it is missing, its instalment when the plan assigns one and
    /// <see cref="ContractAccount.HasInstalmentFor"/> says it is missing, then a payment of the whole
    /// period dated by the ledger date rule. Each charge is guarded on its own, which is what makes
    /// the voided-early-payment case write only the payment. Returns the movements written.
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

        if (AccrualSchedule.InstalmentAmountFor(contract, period) is { } fee && !account.HasInstalmentFor(period))
        {
            written.Add(AccountMovement.ContractualFeeInstalment(Guid.NewGuid(), fee, period, begun.DueDate));
            account.Append(written[^1]);
        }

        // Everything the period holds as of its due date: the rent and the instalment, however they
        // came to be on the account.
        var owing = account.PeriodNet(period, begun.DueDate);

        // Not late, so no recargo: received minus due is negative and the arithmetic already says zero.
        Assert.Equal(0m, RecargoMath.For(owing, begun.DueDate, receivedOn, LeaseTerms));

        var paymentDate = LedgerDating.PaymentDate(receivedOn, chargeDate: begun.DueDate);
        written.Add(AccountMovement.Payment(Guid.NewGuid(), -owing, period, paymentDate));
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
    /// Spec test 68, instalment part: an early payment of a period that carries an instalment brings
    /// the rent AND the instalment onto the account, and the payment settles both, dated on the due
    /// date, with no recargo.
    /// </summary>
    [Fact]
    public void AnEarlyPayment_OfAPeriodWithAnInstalment_WritesTheInstalmentToo()
    {
        var contract = NewContract(new ContractualFeePlan(120_000m, 6));
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(account, contract, new DateOnly(2026, 8, 31));

        // July and August carry instalments 1 and 2, written on their due dates; September is the 3rd.
        Assert.False(account.HasInstalmentFor(September));
        Assert.Equal(3, AccrualSchedule.InstalmentNumberFor(contract, September));

        var written = PayEarly(account, contract, September, ReceivedOn);

        Assert.Equal(
            [MovementKind.RentAccrual, MovementKind.ContractualFeeInstalment, MovementKind.Payment],
            written.Select(m => m.Kind));
        Assert.Equal(SeptemberDue, written[1].OccurredOn);
        Assert.Equal(120_000m, written[1].Amount);
        Assert.Equal(-620_000m, written[2].Amount);
        Assert.Equal(SeptemberDue, written[2].OccurredOn);
        Assert.DoesNotContain(written, m => m.Kind == MovementKind.RecargoCharged);
        Assert.Equal(0m, account.PeriodNet(September, SeptemberDue));
    }

    /// <summary>The later accrual raises neither the rent nor the instalment again.</summary>
    [Fact]
    public void ALaterMaterialisation_RaisesNeitherTheRentNorTheInstalmentAgain()
    {
        var contract = NewContract(new ContractualFeePlan(120_000m, 6));
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(account, contract, new DateOnly(2026, 8, 31));
        PayEarly(account, contract, September, ReceivedOn);

        var raised = Materialise(account, contract, new DateOnly(2026, 9, 11));

        Assert.Equal(0, raised);
        Assert.Single(account.Movements, m => m.Kind == MovementKind.RentAccrual && m.Period == September);
        Assert.Single(account.Movements, m => m.Kind == MovementKind.ContractualFeeInstalment && m.Period == September);
    }

    /// <summary>
    /// The voided-early-payment case (design section 1): voiding reverses the payment but leaves the
    /// rent and instalment it wrote, which are the charge of the period. Paying it again writes only
    /// the payment, because IsChargedFor and HasInstalmentFor say nothing is missing.
    /// </summary>
    [Fact]
    public void AVoidedEarlyPayment_PaidAgain_WritesNoSecondRentOrInstalment()
    {
        var contract = NewContract(new ContractualFeePlan(120_000m, 6));
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(account, contract, new DateOnly(2026, 8, 31));
        var first = PayEarly(account, contract, September, ReceivedOn);

        var voidedOn = new DateOnly(2026, 9, 3);
        var payment = first[^1];
        account.Append(AccountMovement.Correction(
            Guid.NewGuid(), -payment.Amount, LedgerDating.CorrectionDate(voidedOn, payment.OccurredOn), payment.Id, September));
        Assert.False(account.HasLivePaymentFor(September));
        Assert.True(account.IsChargedFor(September));
        Assert.True(account.HasInstalmentFor(September));

        var second = PayEarly(account, contract, September, new DateOnly(2026, 9, 4));

        var only = Assert.Single(second);
        Assert.Equal(MovementKind.Payment, only.Kind);
        Assert.Equal(SeptemberDue, only.OccurredOn);
        Assert.Equal(-620_000m, only.Amount);
        Assert.Equal(0m, account.PeriodNet(September, SeptemberDue));
        Assert.Single(account.Movements, m => m.Kind == MovementKind.RentAccrual && m.Period == September);
        Assert.Single(account.Movements, m => m.Kind == MovementKind.ContractualFeeInstalment && m.Period == September);
    }

    /// <summary>
    /// The early path and the lazy path write the identical instalment row (same kind, period, amount
    /// and date), so there is nothing to reconcile.
    /// </summary>
    [Fact]
    public void TheEarlyInstalmentRow_IsIdenticalToTheRowLazyAccrualWouldWrite()
    {
        var contract = NewContract(new ContractualFeePlan(120_000m, 6));
        var early = new ContractAccount(Guid.NewGuid(), contract.Id);
        var lazy = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(early, contract, new DateOnly(2026, 8, 31));
        Materialise(lazy, contract, new DateOnly(2026, 8, 31));

        PayEarly(early, contract, September, ReceivedOn);
        Materialise(lazy, contract, new DateOnly(2026, 9, 11));

        var earlyFee = Assert.Single(early.Movements, m => m.Kind == MovementKind.ContractualFeeInstalment && m.Period == September);
        var lazyFee = Assert.Single(lazy.Movements, m => m.Kind == MovementKind.ContractualFeeInstalment && m.Period == September);
        Assert.Equal(lazyFee.Amount, earlyFee.Amount);
        Assert.Equal(lazyFee.OccurredOn, earlyFee.OccurredOn);
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
    /// R3-payment-dated-before-settled-movements: a payment entered again after a voiding is never
    /// dated before the latest correction on its period, whatever its received date.
    /// </summary>
    [Fact]
    public void APaymentIsNeverDatedBeforeTheLatestCorrectionOnItsPeriod()
    {
        var chargedOn = new DateOnly(2026, 9, 10);
        var receivedOn = new DateOnly(2026, 9, 15);
        var correctedOn = new DateOnly(2026, 9, 20);

        Assert.Equal(correctedOn, LedgerDating.PaymentDate(receivedOn, chargedOn, correctedOn));
        Assert.Equal(receivedOn, LedgerDating.PaymentDate(receivedOn, chargedOn, new DateOnly(2026, 9, 12)));
        Assert.Equal(receivedOn, LedgerDating.PaymentDate(receivedOn, chargedOn, receivedOn));
        Assert.Equal(receivedOn, LedgerDating.PaymentDate(receivedOn, chargedOn, null));
        Assert.Equal(LedgerDating.PaymentDate(receivedOn, chargedOn), LedgerDating.PaymentDate(receivedOn, chargedOn, null));
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
