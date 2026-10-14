using System.Reflection;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// The collection planner's arithmetic and refusals (cobranza-y-recibos spec Tests 1, 2, 8, 10, 11,
/// 12, 13, plus the planner-level repeats of Tests 6, 7 and 55 and of the instalment rule of Test 58;
/// design sections 1, 2 and 7.3). Pure domain: the planner reads an account and writes nothing, so
/// "writes nothing" is asserted as the account being unchanged and no movements being offered.
/// </summary>
public class CollectionPlannerTests
{
    /// <summary>The fixture the planner, preview and early-payment tests share.</summary>
    internal static class Scenario
    {
        public static readonly RecargoTerms Terms = new(0.02m, graceDays: 0);

        /// <summary>A Saturday in the middle of September: July, August and September are all due.</summary>
        public static readonly DateOnly Today = new(2026, 9, 12);

        public static readonly DateOnly July = new(2026, 7, 1);
        public static readonly DateOnly August = new(2026, 8, 1);
        public static readonly DateOnly September = new(2026, 9, 1);
        public static readonly DateOnly October = new(2026, 10, 1);

        public static ReciboHeader Header() => new("Maria Gomez", ["Juan Perez"], "Departamento 2B");

        public static Contract NewContract(
            decimal? rate = 8m,
            ContractualFeePlan? plan = null,
            DateOnly? start = null,
            decimal rent = 500_000m) =>
            new(
                Guid.NewGuid(),
                start ?? July,
                new DateOnly(2027, 12, 31),
                monthlyRent: rent,
                unitShares: [new UnitShare(Guid.NewGuid(), 100m)],
                honorariosPercentage: rate,
                contractualFeePlan: plan);

        /// <summary>An account with every period due on or before <paramref name="asOf"/> materialised, as the desk does in memory.</summary>
        public static ContractAccount AccountFor(Contract contract, DateOnly asOf)
        {
            var account = new ContractAccount(Guid.NewGuid(), contract.Id);
            Materialise(account, contract, asOf);

            return account;
        }

        public static void Materialise(ContractAccount account, Contract contract, DateOnly asOf)
        {
            foreach (var due in AccrualSchedule.PeriodsDue(contract, asOf))
            {
                if (account.IsChargedFor(due.Period))
                {
                    continue;
                }

                account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), due.Canon, due.Period, due.DueDate));

                if (AccrualSchedule.InstalmentAmountFor(contract, due.Period) is { } fee &&
                    !account.HasInstalmentFor(due.Period))
                {
                    account.Append(AccountMovement.ContractualFeeInstalment(Guid.NewGuid(), fee, due.Period, due.DueDate));
                }
            }
        }

        public static CollectionPreview Plan(
            ContractAccount account,
            Contract contract,
            decimal handed,
            DateOnly? named = null,
            DateOnly? received = null,
            DateOnly? today = null,
            decimal? tasa = null,
            decimal? seguro = null,
            decimal? otros = null) =>
            CollectionPlanner.Plan(
                account,
                contract,
                Terms,
                new CollectionRequest(account.Id, named, received, handed, tasa, seguro, otros),
                today ?? Today,
                Header());

        /// <summary>The preview for the exact ledger figure, found by asking once with an amount that cannot match.</summary>
        public static CollectionPreview PlanExact(
            ContractAccount account,
            Contract contract,
            DateOnly? named = null,
            DateOnly? received = null,
            DateOnly? today = null,
            decimal? tasa = null,
            decimal? seguro = null,
            decimal? otros = null)
        {
            var probe = Plan(account, contract, -1m, named, received, today, tasa, seguro, otros);

            return Plan(account, contract, probe.LedgerFigure, named, received, today, tasa, seguro, otros);
        }

        public static void Apply(ContractAccount account, IEnumerable<AccountMovement> movements)
        {
            foreach (var movement in movements)
            {
                account.Append(movement);
            }
        }
    }

    private static readonly DateOnly Today = Scenario.Today;
    private static readonly DateOnly July = Scenario.July;
    private static readonly DateOnly August = Scenario.August;
    private static readonly DateOnly September = Scenario.September;

    // Test 1
    [Fact]
    public void APartialAmount_IsRefused_AndWritesNothing()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);
        var movementsBefore = account.Movements.Count;
        var balanceBefore = account.LedgerBalance(Today);

        var preview = Scenario.Plan(account, contract, handed: 829_999m, named: August);

        Assert.Equal(CollectionRefusal.AmountNotTotal, preview.Refusal);
        Assert.False(preview.CanCommit);
        Assert.Equal(830_000m, preview.LedgerFigure);
        Assert.Throws<InvalidOperationException>(() => CollectionPlanner.MovementsToWrite(account, contract, preview));
        Assert.Equal(movementsBefore, account.Movements.Count);
        Assert.Equal(balanceBefore, account.LedgerBalance(Today));
    }

    // Test 2
    [Fact]
    public void AnExcessAmount_IsRefused_AndWritesNothing()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);
        var movementsBefore = account.Movements.Count;

        var preview = Scenario.Plan(account, contract, handed: 830_001m, named: August);

        Assert.Equal(CollectionRefusal.AmountNotTotal, preview.Refusal);
        Assert.False(preview.CanCommit);
        Assert.Throws<InvalidOperationException>(() => CollectionPlanner.MovementsToWrite(account, contract, preview));
        Assert.Equal(movementsBefore, account.Movements.Count);
    }

    [Fact]
    public void TheExactLedgerFigure_IsAccepted()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.Plan(account, contract, handed: 830_000m, named: August);

        Assert.Null(preview.Refusal);
        Assert.True(preview.CanCommit);
    }

    // Test 8
    [Fact]
    public void TheRecargoCharged_EqualsTheRecargoProjectedAsOfTheReceivedDate()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.PlanExact(account, contract, named: August);
        var written = CollectionPlanner.MovementsToWrite(account, contract, preview);

        // August fell due on the 10th: 33 days at 2% of 500,000.
        var recargo = Assert.Single(written, m => m.Kind == MovementKind.RecargoCharged);
        Assert.Equal(330_000m, recargo.Amount);
        Assert.Equal(account.RecargoProjectedFor(August, Today, Scenario.Terms), recargo.Amount);
        Assert.Equal(330_000m, preview.Locatario!.Lines.Single(l => l.Kind == ReciboLineKind.Recargo).Amount);
    }

    // Test 8
    [Fact]
    public void TheRecargoCharged_IsTruncatedToAWholePeso()
    {
        // 333,333 x 2% x 33 days = 219,999.78.
        var contract = Scenario.NewContract(rent: 333_333m);
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.PlanExact(account, contract, named: August);
        var recargo = Assert.Single(
            CollectionPlanner.MovementsToWrite(account, contract, preview), m => m.Kind == MovementKind.RecargoCharged);

        Assert.Equal(219_999m, recargo.Amount);
        Assert.Equal(333_333m + 219_999m, preview.LedgerFigure);
    }

    // Test 10
    [Fact]
    public void AZeroRecargo_WritesNoRecargoCharged_AndPrintsNoRecargoLine()
    {
        var contract = Scenario.NewContract();
        var dueDay = new DateOnly(2026, 9, 10);
        var account = Scenario.AccountFor(contract, dueDay);

        // Paid on the due day itself: nothing late yet.
        var preview = Scenario.PlanExact(account, contract, named: September, today: dueDay);
        var written = CollectionPlanner.MovementsToWrite(account, contract, preview);

        Assert.Null(preview.Refusal);
        Assert.Equal(500_000m, preview.LedgerFigure);
        Assert.DoesNotContain(written, m => m.Kind == MovementKind.RecargoCharged);
        Assert.DoesNotContain(preview.Locatario!.Lines, l => l.Kind == ReciboLineKind.Recargo);
        Assert.DoesNotContain(preview.Propietario!.Lines, l => l.Kind == ReciboLineKind.Recargo);
    }

    // Test 11
    [Fact]
    public void WithNoReceivedDate_ThePreviewResolvesItToToday_AndRecordsWhichDay()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.PlanExact(account, contract, named: August);

        Assert.Equal(Today, preview.ReceivedOn);
        Assert.Equal(Today, preview.ResolvedToday);
    }

    // Test 11
    [Fact]
    public void TheRequest_OffersNoWayToSupplyARecordedAt()
    {
        var names = typeof(CollectionRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);

        Assert.DoesNotContain(names, n => n.Contains("Recorded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nameof(CollectionRequest.ReceivedOn), names);
    }

    // Test 12
    [Fact]
    public void AFutureReceivedDate_IsRefused()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.Plan(account, contract, handed: 1m, named: August, received: Today.AddDays(1));

        Assert.Equal(CollectionRefusal.ReceivedOnInFuture, preview.Refusal);
        Assert.False(preview.CanCommit);
    }

    // Test 12
    [Fact]
    public void AReceivedDateMoreThanSevenDaysBack_IsRefused()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.Plan(
            account, contract, handed: 1m, named: August, received: Today.AddDays(-(CollectionRules.BackDateBoundDays + 1)));

        Assert.Equal(8, Today.DayNumber - preview.ReceivedOn.DayNumber);
        Assert.Equal(CollectionRefusal.ReceivedOnBeyondBound, preview.Refusal);
    }

    // Test 12: the bound itself is seven, written once in CollectionRules.
    [Fact]
    public void AReceivedDateExactlySevenDaysBack_IsAccepted()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.PlanExact(account, contract, named: August, received: Today.AddDays(-7));

        Assert.Equal(7, Today.DayNumber - preview.ReceivedOn.DayNumber);
        Assert.Null(preview.Refusal);
        Assert.Equal(7, CollectionRules.BackDateBoundDays);
    }

    // Test 13
    [Fact]
    public void ABackDatedReceivedDate_ChargesTheRecargoOfThatDate_AndTheReciboShowsIt()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);
        var fiveDaysBack = Today.AddDays(-5);

        var todays = Scenario.PlanExact(account, contract, named: August);
        var backDated = Scenario.PlanExact(account, contract, named: August, received: fiveDaysBack);

        // 33 days late today, 28 days late on the 7th.
        Assert.Equal(330_000m, todays.Locatario!.Lines.Single(l => l.Kind == ReciboLineKind.Recargo).Amount);
        Assert.Equal(280_000m, backDated.Locatario!.Lines.Single(l => l.Kind == ReciboLineKind.Recargo).Amount);
        Assert.True(backDated.LedgerFigure < todays.LedgerFigure);

        var issued = ReciboPair.Issue(31_001, August, backDated.ReceivedOn, Scenario.Header(), new ReciboDraftPair(backDated.Locatario!, backDated.Propietario!));
        Assert.Equal(fiveDaysBack, issued.Locatario.ReceivedOn);
        Assert.Equal(fiveDaysBack, issued.Propietario.ReceivedOn);
    }

    // Test 6, through the planner
    [Fact]
    public void ASettledPeriod_IsRefusedAsAlreadySettled()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);
        var paid = Scenario.PlanExact(account, contract, named: August);
        Scenario.Apply(account, CollectionPlanner.MovementsToWrite(account, contract, paid));
        var movementsAfterPayment = account.Movements.Count;

        var again = Scenario.Plan(account, contract, handed: 1m, named: August);

        Assert.Equal(CollectionRefusal.PeriodAlreadySettled, again.Refusal);
        Assert.Equal(movementsAfterPayment, account.Movements.Count);
    }

    // Test 6, through the planner
    [Fact]
    public void AMonthThatHasNotBegun_IsRefusedAsNotBegun()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.Plan(account, contract, handed: 1m, named: Scenario.October);

        Assert.Equal(CollectionRefusal.PeriodNotBegun, preview.Refusal);
    }

    // Test 6, through the planner
    [Theory]
    [InlineData(2026, 6, 1)] // before the contract starts
    [InlineData(2026, 8, 15)] // not the first of a month
    public void APeriodNotOnTheContract_IsRefusedAsNotOnTheContract(int year, int month, int day)
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.Plan(account, contract, handed: 1m, named: new DateOnly(year, month, day));

        Assert.Equal(CollectionRefusal.PeriodNotOnContract, preview.Refusal);
    }

    // Test 6, through the planner: with nothing named and nothing owed, nothing is payable.
    [Fact]
    public void WithNothingNamedAndNothingPayable_TheRefusalIsNothingPayable()
    {
        var contract = Scenario.NewContract(start: Scenario.August);
        var account = Scenario.AccountFor(contract, Today);

        foreach (var period in new[] { Scenario.August, Scenario.September })
        {
            var preview = Scenario.PlanExact(account, contract);
            Assert.Equal(period, preview.Period);
            Scenario.Apply(account, CollectionPlanner.MovementsToWrite(account, contract, preview));
        }

        var none = Scenario.Plan(account, contract, handed: 1m);

        // September is in course and paid, so no month is payable.
        Assert.Equal(CollectionRefusal.NothingPayable, none.Refusal);
        Assert.Null(none.Period);
    }

    // Test 7, through the planner
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFrozenAccount_IsRefused_WhetherOrNotAPeriodIsNamed(bool named)
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);
        account.Append(AccountMovement.RecargoFrozen(Guid.NewGuid(), 50_000m, new DateOnly(2026, 9, 1)));

        var preview = Scenario.Plan(account, contract, handed: 1m, named: named ? August : null);

        Assert.Equal(CollectionRefusal.AccountFrozen, preview.Refusal);
    }

    [Fact]
    public void ANegativeTasaMunicipal_IsRefused_BeforeAnyFigureIsBuilt()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.Plan(account, contract, handed: 830_000m, named: August, tasa: -1m);

        Assert.Equal(CollectionRefusal.NegativeTasaMunicipal, preview.Refusal);
        Assert.Null(preview.Locatario);
    }

    [Fact]
    public void WithNoPeriodNamed_TheOldestOwedOneIsSettled()
    {
        var contract = Scenario.NewContract();
        var account = Scenario.AccountFor(contract, Today);

        var preview = Scenario.PlanExact(account, contract);

        Assert.Equal(July, preview.Period);
        Assert.False(preview.PeriodWasEarly);
    }

    // Test 55, through the planner (task 1.7)
    [Fact]
    public void AnOpeningBalanceMonth_WritesNoRecargoCharged()
    {
        var contract = Scenario.NewContract();
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 160_000m, new DateOnly(2026, 9, 1), August));
        Scenario.Materialise(account, contract, Today);

        var preview = Scenario.PlanExact(account, contract, named: August);
        var written = CollectionPlanner.MovementsToWrite(account, contract, preview);

        Assert.Null(preview.Refusal);
        Assert.Equal(160_000m, preview.LedgerFigure);
        Assert.Equal([MovementKind.Payment], written.Select(m => m.Kind));
        Assert.Equal(-160_000m, written.Single().Amount);
        Assert.Equal([ReciboLineKind.SaldoDeudor], preview.Locatario!.Lines.Select(l => l.Kind));
    }

    // Test 58, through the planner (task 2.4): the instalment rides with the rent and is never a payment of its own.
    [Fact]
    public void AnInstalment_CannotBePaidAlone()
    {
        var contract = Scenario.NewContract(plan: new ContractualFeePlan(90_000m, 3));
        var account = Scenario.AccountFor(contract, Today);

        var exact = Scenario.PlanExact(account, contract, named: July);
        var rentOnly = Scenario.Plan(account, contract, handed: exact.LedgerFigure - 90_000m, named: July);
        var instalmentOnly = Scenario.Plan(account, contract, handed: 90_000m, named: July);

        Assert.Null(exact.Refusal);
        Assert.Equal(CollectionRefusal.AmountNotTotal, rentOnly.Refusal);
        Assert.Equal(CollectionRefusal.AmountNotTotal, instalmentOnly.Refusal);
        Assert.Contains(exact.Locatario!.Lines, l => l.Kind == ReciboLineKind.ContractualFeeInstalment && l.Amount == 90_000m);
        Assert.DoesNotContain(typeof(CollectionRequest).GetProperties(), p => p.Name.Contains("Instalment", StringComparison.Ordinal));
    }
}
