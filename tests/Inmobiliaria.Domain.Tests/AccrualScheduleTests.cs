using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

public class AccrualScheduleTests
{
    private static readonly DateOnly JanuaryFirst = new(2026, 1, 1);

    /// <summary>Spec test 34: a contract created without a due day reads the agency's 10th.</summary>
    [Fact]
    public void AContractCreatedWithoutADueDay_FallsDueOnTheTenth()
    {
        var contract = ContractTestFactory.CreateActive();

        Assert.Equal(10, contract.DueDay);
        Assert.Equal(Contract.DefaultDueDay, contract.DueDay);
    }

    /// <summary>Spec test 35: the owner can set another day, and the contract keeps it.</summary>
    [Fact]
    public void AContractCreatedWithADueDay_KeepsIt()
    {
        var contract = ContractTestFactory.CreateActive(dueDay: 5);

        Assert.Equal(5, contract.DueDay);
    }

    /// <summary>Spec test 36: a day that is not a day of the month is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(32)]
    public void ADueDayThatIsNotADayOfTheMonth_IsRefused(int dueDay)
    {
        Assert.Throws<ArgumentException>(() => ContractTestFactory.CreateActive(dueDay: dueDay));
    }

    /// <summary>
    /// The 29th through the 31st are accepted, because a lease that says the 31st is a lease the
    /// system must take. The short month is handled when the due date is computed, not by
    /// refusing the contract.
    /// </summary>
    [Theory]
    [InlineData(29)]
    [InlineData(31)]
    public void ADueDayLongerThanSomeMonths_IsStillAValidContract(int dueDay)
    {
        var contract = ContractTestFactory.CreateActive(dueDay: dueDay);

        Assert.Equal(dueDay, contract.DueDay);
    }

    /// <summary>
    /// A due day past the end of its month clamps to that month's last day. Rolling into March
    /// instead would charge a month of mora the tenant had no way to avoid.
    /// </summary>
    [Fact]
    public void ADueDayPastTheEndOfTheMonth_ClampsToItsLastDay()
    {
        // 2026 is not a leap year, so February has 28 days.
        Assert.Equal(new DateOnly(2026, 2, 28), AccrualSchedule.DueDateFor(new DateOnly(2026, 2, 1), 31));
        Assert.Equal(new DateOnly(2026, 4, 30), AccrualSchedule.DueDateFor(new DateOnly(2026, 4, 1), 31));

        // And a day that fits is left exactly alone.
        Assert.Equal(new DateOnly(2026, 3, 31), AccrualSchedule.DueDateFor(new DateOnly(2026, 3, 1), 31));
    }

    [Fact]
    public void AContractWithNoAdjustments_AccruesEveryDuePeriodAtItsCanon()
    {
        var contract = ContractTestFactory.CreateActive(
            startDate: JanuaryFirst, monthlyRent: 500_000m);

        var due = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 3, 15));

        Assert.Equal(3, due.Count);
        Assert.All(due, period => Assert.Equal(500_000m, period.Canon));
        Assert.Equal(
            [new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)],
            due.Select(p => p.Period));
    }

    /// <summary>
    /// A period that has not reached its due date is not due yet. Asked on the 5th with a due day
    /// of the 10th, March has not fallen due even though March has started.
    /// </summary>
    [Fact]
    public void APeriodBeforeItsDueDate_IsNotDueYet()
    {
        var contract = ContractTestFactory.CreateActive(startDate: JanuaryFirst);

        var due = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 3, 5));

        Assert.Equal(2, due.Count);
        Assert.DoesNotContain(new DateOnly(2026, 3, 1), due.Select(p => p.Period));
    }

    /// <summary>
    /// A lease signed after its month's due day has already passed owes nothing for that month.
    /// The contract did not exist on the day the rent would have fallen due.
    ///
    /// This is NOT proration. The specification says nothing about charging a part-month for the
    /// days actually occupied, and this schedule does not invent one: if the agency does charge
    /// it, somebody records that as its own movement. The open question is noted for the owner.
    /// </summary>
    [Fact]
    public void AContractSignedAfterItsDueDayHasPassed_OwesNothingForThatMonth()
    {
        var contract = ContractTestFactory.CreateActive(
            startDate: new DateOnly(2026, 1, 20), dueDay: 10);

        var due = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 2, 28));

        Assert.Single(due);
        Assert.Equal(new DateOnly(2026, 2, 1), due[0].Period);
        Assert.Equal(new DateOnly(2026, 2, 10), due[0].DueDate);
    }

    /// <summary>
    /// Spec tests 11 and 12: three periods accrued before a late adjustment keep the old canon,
    /// and the first period taking effect after it uses the new one. A period's charge never
    /// moves once it has accrued — the thing the specification forbids outright.
    ///
    /// The canon cannot be read off Contract.MonthlyRent for a past period, because confirming
    /// the adjustment overwrote it. Here MonthlyRent is 600,000 by the end, and January through
    /// March must still read 500,000.
    /// </summary>
    [Fact]
    public void PeriodsAccruedBeforeALateAdjustment_KeepTheOldCanon()
    {
        var contract = ContractTestFactory.CreateActive(
            startDate: JanuaryFirst, monthlyRent: 500_000m);

        ContractTestFactory.ConfirmAdjustment(
            contract, previousCanon: 500_000m, percentage: 20m, effectiveDate: new DateOnly(2026, 4, 1));

        // The adjustment really did overwrite the current canon — otherwise this test would pass
        // for the wrong reason.
        Assert.Equal(600_000m, contract.MonthlyRent);

        var due = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 4, 30));

        Assert.Equal(4, due.Count);
        Assert.Equal(500_000m, due[0].Canon);
        Assert.Equal(500_000m, due[1].Canon);
        Assert.Equal(500_000m, due[2].Canon);
        Assert.Equal(600_000m, due[3].Canon);
    }

    /// <summary>
    /// With two adjustments, each period reads the canon of whichever one was in force when it
    /// fell due — including the periods before the first adjustment, whose canon survives only as
    /// that adjustment's PreviousCanon.
    /// </summary>
    [Fact]
    public void WithTwoAdjustments_EachPeriodReadsTheCanonInForceWhenItFellDue()
    {
        var contract = ContractTestFactory.CreateActive(
            startDate: JanuaryFirst, monthlyRent: 500_000m);

        ContractTestFactory.ConfirmAdjustment(
            contract, previousCanon: 500_000m, percentage: 20m, effectiveDate: new DateOnly(2026, 3, 1));
        ContractTestFactory.ConfirmAdjustment(
            contract, previousCanon: 600_000m, percentage: 10m, effectiveDate: new DateOnly(2026, 5, 1));

        var due = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 5, 31));

        Assert.Equal(5, due.Count);
        Assert.Equal(500_000m, due[0].Canon);
        Assert.Equal(500_000m, due[1].Canon);
        Assert.Equal(600_000m, due[2].Canon);
        Assert.Equal(600_000m, due[3].Canon);
        Assert.Equal(660_000m, due[4].Canon);
    }

    /// <summary>
    /// Spec test 25: a contract that ended produces no period after its end date. This is what
    /// makes an ended account need no closed flag — there is simply nothing further to come due
    /// (design Decision 6).
    /// </summary>
    [Fact]
    public void AnEndedContract_HasNoPeriodsDueAfterItsEndDate()
    {
        var contract = ContractTestFactory.CreateActive(startDate: JanuaryFirst);
        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 3, 20));

        // Asked long after the end date, and well past several later due days.
        var due = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 12, 31));

        Assert.Equal(3, due.Count);
        Assert.All(due, period => Assert.True(period.DueDate <= new DateOnly(2026, 3, 20)));
    }

    /// <summary>
    /// The account itself carries no flag, so the proof that it is settled is that the schedule
    /// stops producing work. Asked repeatedly after the end date, the answer never grows.
    /// </summary>
    [Fact]
    public void AnEndedContract_ProducesTheSameAnswerHoweverLateItIsAsked()
    {
        var contract = ContractTestFactory.CreateActive(startDate: JanuaryFirst);
        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 3, 20));

        var soonAfter = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 4, 1));
        var yearsAfter = AccrualSchedule.PeriodsDue(contract, new DateOnly(2030, 1, 1));

        Assert.Equal(soonAfter.Count, yearsAfter.Count);
    }

    /// <summary>
    /// Spec tests 18 and 38: the due day is what mora is measured from, so two contracts equally
    /// overdue by the calendar are NOT equally overdue in money. A due day of the 5th has accrued
    /// five more days than a due day of the 10th by the same date.
    /// </summary>
    [Fact]
    public void TwoContractsWithDifferentDueDays_AccrueDifferentMora()
    {
        var onTheFifth = ContractTestFactory.CreateActive(
            startDate: JanuaryFirst, monthlyRent: 500_000m, dueDay: 5);
        var onTheTenth = ContractTestFactory.CreateActive(
            startDate: JanuaryFirst, monthlyRent: 500_000m, dueDay: 10);

        var asOf = new DateOnly(2026, 1, 20);
        var terms = new RecargoTerms(0.02m, graceDays: 0);

        var earlier = AccrualSchedule.PeriodsDue(onTheFifth, asOf).Single();
        var later = AccrualSchedule.PeriodsDue(onTheTenth, asOf).Single();

        Assert.Equal(new DateOnly(2026, 1, 5), earlier.DueDate);
        Assert.Equal(new DateOnly(2026, 1, 10), later.DueDate);

        // 15 days against 10, on the same canon.
        Assert.Equal(150_000m, RecargoMath.For(earlier.Canon, earlier.DueDate, asOf, terms));
        Assert.Equal(100_000m, RecargoMath.For(later.Canon, later.DueDate, asOf, terms));
    }

    /// <summary>
    /// Asked before anything has fallen due, the schedule produces nothing rather than throwing.
    /// A brand new contract is the ordinary case, not an error.
    /// </summary>
    [Fact]
    public void ABrandNewContract_HasNothingDueYet()
    {
        var contract = ContractTestFactory.CreateActive(startDate: new DateOnly(2026, 1, 1));

        var due = AccrualSchedule.PeriodsDue(contract, new DateOnly(2026, 1, 3));

        Assert.Empty(due);
    }

    [Fact]
    public void PeriodsDue_RequiresAContract()
    {
        Assert.Throws<ArgumentNullException>(
            () => AccrualSchedule.PeriodsDue(null!, JanuaryFirst));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void DueDateFor_RefusesADayThatIsNotADayOfTheMonth(int dueDay)
    {
        Assert.Throws<ArgumentException>(() => AccrualSchedule.DueDateFor(JanuaryFirst, dueDay));
    }
}
