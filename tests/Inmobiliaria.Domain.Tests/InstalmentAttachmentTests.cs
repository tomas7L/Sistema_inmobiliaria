using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// The honorarios contractuales instalment rides with the rent (cobranza-y-recibos spec Tests 58 and
/// 59; design D19): instalment k attaches to the k-th rent period the schedule produces, is paid
/// with that rent, cannot be paid alone, is never raised twice, and is not raised for a period due
/// on or before a pre-existing contract's cutover date.
/// </summary>
public class InstalmentAttachmentTests
{
    private static readonly ContractualFeePlan ThreeOf120k = new(120_000m, 3);

    private static DateOnly Month(int year, int month) => new(year, month, 1);

    private static Contract NewContract(
        DateOnly? start = null,
        DateOnly? cutover = null,
        ContractualFeePlan? plan = null) =>
        new(
            Guid.NewGuid(),
            start ?? new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(Guid.NewGuid(), 100m)],
            cutoverDate: cutover,
            contractualFeePlan: plan);

    /// <summary>
    /// What the materialiser writes, assembled from the domain members it uses: the rent of every due
    /// period not yet charged, and the instalment beside it when the plan assigns one.
    /// </summary>
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

            if (AccrualSchedule.InstalmentAmountFor(contract, due.Period) is { } amount &&
                !account.HasInstalmentFor(due.Period))
            {
                account.Append(AccountMovement.ContractualFeeInstalment(
                    Guid.NewGuid(), amount, due.Period, due.DueDate));
                written++;
            }
        }

        return written;
    }

    private static IEnumerable<DateOnly> InstalmentPeriods(ContractAccount account) =>
        account.Movements
            .Where(m => m.Kind == MovementKind.ContractualFeeInstalment)
            .Select(m => m.Period!.Value)
            .OrderBy(p => p);

    /// <summary>Spec test 58: instalment k attaches to the k-th rent period, and only the first count of them.</summary>
    [Fact]
    public void InstalmentK_AttachesToTheKthRentPeriod_ForAsManyPeriodsAsThePlanHas()
    {
        var contract = NewContract(plan: ThreeOf120k);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        Materialise(account, contract, new DateOnly(2026, 6, 15));

        Assert.Equal([Month(2026, 1), Month(2026, 2), Month(2026, 3)], InstalmentPeriods(account));
        Assert.All(
            account.Movements.Where(m => m.Kind == MovementKind.ContractualFeeInstalment),
            m => Assert.Equal(120_000m, m.Amount));
    }

    /// <summary>
    /// Instalment 1 rides with the first period that actually charges rent, so a lease signed after the
    /// due day (design Q3 default) takes it with the following month.
    /// </summary>
    [Fact]
    public void InstalmentOne_RidesWithTheFirstMonthThatActuallyChargesRent()
    {
        // Signed on the 20th: January's 10th came before the contract existed.
        var contract = NewContract(start: new DateOnly(2026, 1, 20), plan: ThreeOf120k);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        Materialise(account, contract, new DateOnly(2026, 6, 15));

        Assert.Equal([Month(2026, 2), Month(2026, 3), Month(2026, 4)], InstalmentPeriods(account));
    }

    [Fact]
    public void AContractWithNoPlan_RaisesNoInstalment()
    {
        var contract = NewContract();
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        Materialise(account, contract, new DateOnly(2026, 6, 15));

        Assert.Empty(InstalmentPeriods(account));
        Assert.Null(AccrualSchedule.InstalmentAmountFor(contract, Month(2026, 1)));
    }

    [Fact]
    public void AMonthTheScheduleNeverProduces_CarriesNoInstalment()
    {
        var contract = NewContract(start: new DateOnly(2026, 1, 20), plan: ThreeOf120k);

        Assert.Null(AccrualSchedule.InstalmentAmountFor(contract, Month(2026, 1)));
        Assert.Null(AccrualSchedule.InstalmentAmountFor(contract, Month(2025, 12)));
        Assert.Null(AccrualSchedule.InstalmentAmountFor(contract, new DateOnly(2026, 3, 15)));
    }

    /// <summary>The cap at the plan's count: once the plan has one instalment the second period has none.</summary>
    [Fact]
    public void InstalmentNumberFor_IsNullBeyondThePlansCount()
    {
        var contract = NewContract(plan: new ContractualFeePlan(100_000m, 2));

        Assert.Equal(1, AccrualSchedule.InstalmentNumberFor(contract, Month(2026, 1)));
        Assert.Equal(2, AccrualSchedule.InstalmentNumberFor(contract, Month(2026, 2)));
        Assert.Null(AccrualSchedule.InstalmentNumberFor(contract, Month(2026, 3)));
    }

    /// <summary>Spec test 58: paid with the rent: the period is one figure, rent plus instalment, and one payment settles it.</summary>
    [Fact]
    public void TheInstalment_IsPaidWithTheRent_AndOnePaymentSettlesBoth()
    {
        var contract = NewContract(plan: ThreeOf120k);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        Materialise(account, contract, new DateOnly(2026, 1, 20));
        var january = Month(2026, 1);
        var asOf = new DateOnly(2026, 1, 20);

        Assert.Equal(620_000m, account.PeriodNet(january, asOf));

        account.Append(AccountMovement.Payment(Guid.NewGuid(), -620_000m, january, asOf));

        Assert.Equal(0m, account.PeriodNet(january, asOf));
        Assert.True(account.HasLivePaymentFor(january));
    }

    /// <summary>
    /// Spec test 58, account level: the instalment cannot be paid alone. A payment of the rent only
    /// leaves the instalment owing, and a payment of the instalment only leaves the rent owing, so
    /// neither settles the period. The planner-level repeat is task 4.4.
    /// </summary>
    [Fact]
    public void TheInstalment_CannotBePaidAlone_APaymentBelowThePeriodNetDoesNotSettleIt()
    {
        var contract = NewContract(plan: ThreeOf120k);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        var asOf = new DateOnly(2026, 1, 20);
        Materialise(account, contract, asOf);
        var january = Month(2026, 1);

        account.Append(AccountMovement.Payment(Guid.NewGuid(), -500_000m, january, asOf));

        Assert.Equal(120_000m, account.PeriodNet(january, asOf));
        Assert.Equal(january, account.OldestUnpaidPeriod());
    }

    /// <summary>Spec test 58: never raised twice, by the domain and by the materialisation step.</summary>
    [Fact]
    public void AnInstalment_IsNeverRaisedTwice()
    {
        var contract = NewContract(plan: ThreeOf120k);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        var asOf = new DateOnly(2026, 6, 15);

        Materialise(account, contract, asOf);
        var again = Materialise(account, contract, asOf);

        Assert.Equal(0, again);
        Assert.Equal(3, account.Movements.Count(m => m.Kind == MovementKind.ContractualFeeInstalment));
    }

    [Fact]
    public void TheAccount_RefusesASecondInstalmentForOnePeriod()
    {
        var contract = NewContract(plan: ThreeOf120k);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        var january = Month(2026, 1);
        account.Append(AccountMovement.ContractualFeeInstalment(
            Guid.NewGuid(), 120_000m, january, new DateOnly(2026, 1, 10)));

        Assert.Throws<InvalidOperationException>(() =>
            account.Append(AccountMovement.ContractualFeeInstalment(
                Guid.NewGuid(), 120_000m, january, new DateOnly(2026, 1, 10))));

        Assert.Single(account.Movements);
    }

    /// <summary>
    /// Spec test 59: no instalment for a period due on or before the cutover date, including one due
    /// exactly on it, and instalments 1 and 2 are skipped, not renumbered onto later months.
    /// </summary>
    [Fact]
    public void ForAPreExistingContract_PeriodsDueOnOrBeforeTheCutoverTakeNoInstalment_AndNothingIsRenumbered()
    {
        // January is due on the 10th, February on the 10th, which is exactly the cutover date.
        var contract = NewContract(cutover: new DateOnly(2026, 2, 10), plan: ThreeOf120k);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        Materialise(account, contract, new DateOnly(2026, 6, 15));

        // Instalments 1 and 2 belonged to January and February and are skipped; instalment 3 stays
        // on March, the third period; there is no instalment 4.
        Assert.Equal([Month(2026, 3)], InstalmentPeriods(account));
        Assert.DoesNotContain(account.Movements, m => m.Period <= Month(2026, 2));
    }

    [Fact]
    public void InstalmentAmountFor_IsNullForAPeriodDueOnOrBeforeTheCutover()
    {
        var contract = NewContract(cutover: new DateOnly(2026, 2, 10), plan: ThreeOf120k);

        Assert.Null(AccrualSchedule.InstalmentAmountFor(contract, Month(2026, 1)));
        Assert.Null(AccrualSchedule.InstalmentAmountFor(contract, Month(2026, 2)));
        Assert.Equal(120_000m, AccrualSchedule.InstalmentAmountFor(contract, Month(2026, 3)));
    }
}
