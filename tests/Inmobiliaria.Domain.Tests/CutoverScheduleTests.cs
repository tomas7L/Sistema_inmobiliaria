using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// A contract that predates the system accrues nothing due on or before its cutover date
/// (cobranza-y-recibos spec Test 71, Domain part; design D17). The schedule reads the date from the
/// contract, so no caller (materialiser, mora worklist, payment) can forget it.
/// </summary>
public class CutoverScheduleTests
{
    private static readonly DateOnly JanuaryFirst = new(2026, 1, 1);
    private static readonly DateOnly MarchTenth = new(2026, 3, 10);

    private static Contract NewContract(
        DateOnly start,
        DateOnly? cutover = null,
        int dueDay = Contract.DefaultDueDay) =>
        new(
            Guid.NewGuid(),
            start,
            new DateOnly(2027, 12, 31),
            monthlyRent: 100_000m,
            unitShares: [new UnitShare(Guid.NewGuid(), 100m)],
            dueDay: dueDay,
            cutoverDate: cutover);

    private static DateOnly Month(int year, int month) => new(year, month, 1);

    /// <summary>Spec test 71: no period due on or before the cutover date, including one due exactly on it.</summary>
    [Fact]
    public void PeriodsDue_YieldsNothingDueOnOrBeforeTheCutoverDate()
    {
        var contract = NewContract(JanuaryFirst, cutover: MarchTenth);

        var due = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 6, 15));

        // January, February and March fall due on the 10th; March's is exactly the cutover date.
        Assert.Equal([Month(2026, 4), Month(2026, 5), Month(2026, 6)], due.Select(p => p.Period));
        Assert.All(due, p => Assert.True(p.DueDate > MarchTenth));
    }

    [Fact]
    public void ThePeriodDueTheDayAfterTheCutoverDate_IsProduced()
    {
        var contract = NewContract(JanuaryFirst, cutover: new DateOnly(2026, 3, 9));

        var due = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 3, 10));

        Assert.Equal(Month(2026, 3), Assert.Single(due).Period);
    }

    /// <summary>A contract with no cutover date behaves exactly as it did before this change.</summary>
    [Fact]
    public void AContractWithNoCutoverDate_ProducesEveryPeriodFromItsStart()
    {
        var contract = NewContract(JanuaryFirst);

        Assert.Null(contract.CutoverDate);
        Assert.Equal(6, AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 6, 15)).Count);
    }

    /// <summary>
    /// Spec test 71 and Test 59: instalment k belongs to the k-th scheduled period from the
    /// contract's start. The periods skipped by the cutover are not renumbered onto later ones.
    /// </summary>
    [Fact]
    public void InstalmentNumberFor_CountsFromTheFirstScheduledPeriod_IncludingThoseBeforeTheCutover()
    {
        var contract = NewContract(JanuaryFirst, cutover: MarchTenth);

        Assert.Equal(1, AccrualSchedule.InstalmentNumberFor(contract, Month(2026, 1)));
        Assert.Equal(3, AccrualSchedule.InstalmentNumberFor(contract, Month(2026, 3)));
        Assert.Equal(4, AccrualSchedule.InstalmentNumberFor(contract, Month(2026, 4)));
        Assert.Equal(6, AccrualSchedule.InstalmentNumberFor(contract, Month(2026, 6)));
    }

    /// <summary>A month the contract never charges has no instalment number.</summary>
    [Fact]
    public void InstalmentNumberFor_IsNullForAMonthTheScheduleNeverProduces()
    {
        // Signed on the 20th, so January's 10th due date came before the contract existed.
        var contract = NewContract(new DateOnly(2026, 1, 20));

        Assert.Null(AccrualSchedule.InstalmentNumberFor(contract, Month(2026, 1)));
        Assert.Null(AccrualSchedule.InstalmentNumberFor(contract, Month(2025, 12)));
        Assert.Null(AccrualSchedule.InstalmentNumberFor(contract, new DateOnly(2026, 3, 15)));
        Assert.Equal(1, AccrualSchedule.InstalmentNumberFor(contract, Month(2026, 2)));
    }

    /// <summary>A month that has begun but not fallen due is "begun, not yet due": payable early (design D1).</summary>
    [Fact]
    public void PeriodsBegun_IncludesTheMonthInCourse_BeforeItsDueDate()
    {
        var contract = NewContract(JanuaryFirst);
        var asOf = new DateOnly(2026, 3, 5);

        var due = AccrualSchedule.PeriodsDue(contract, asOf).Select(p => p.Period);
        var begun = AccrualSchedule.PeriodsBegun(contract, asOf);

        Assert.Equal([Month(2026, 1), Month(2026, 2)], due);
        Assert.Equal([Month(2026, 1), Month(2026, 2), Month(2026, 3)], begun.Select(p => p.Period));

        // The same record the lazy accrual would write on the due date.
        var march = begun[^1];
        Assert.Equal(new DateOnly(2026, 3, 10), march.DueDate);
        Assert.Equal(100_000m, march.Canon);
    }

    [Fact]
    public void PeriodsBegun_ExcludesAMonthThatHasNotBegun()
    {
        var contract = NewContract(JanuaryFirst);

        var begun = AccrualSchedule.PeriodsBegun(contract, new DateOnly(2026, 2, 28));

        Assert.DoesNotContain(Month(2026, 3), begun.Select(p => p.Period));
    }

    [Fact]
    public void PeriodsBegun_AppliesTheSameCutoverFloor()
    {
        var contract = NewContract(JanuaryFirst, cutover: MarchTenth);

        // March has begun, but it falls due on the cutover date, so it belongs to the opening balance.
        var begun = AccrualSchedule.PeriodsBegun(contract, new DateOnly(2026, 3, 5));

        Assert.Empty(begun);
    }

    [Fact]
    public void PeriodsBegun_ExcludesAMonthWhoseDueDateCameAfterTheContractEnded()
    {
        var contract = NewContract(JanuaryFirst);
        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 3, 5));

        var begun = AccrualSchedule.PeriodsBegun(contract, new DateOnly(2026, 3, 5));

        Assert.Equal([Month(2026, 1), Month(2026, 2)], begun.Select(p => p.Period));
    }
}
