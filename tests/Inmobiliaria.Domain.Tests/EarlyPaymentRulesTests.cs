using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// The early payment through the planner (cobranza-y-recibos spec Tests 68, 73 and 74; design
/// section 1, D1, D2 and D26). The planner offers the movements a payment writes; these tests append
/// them to an account the way the desk will, so a voided early payment paid again is checked on the
/// ledger itself and not on a restated rule.
/// </summary>
public class EarlyPaymentRulesTests
{
    private static readonly DateOnly September = CollectionPlannerTests.Scenario.September;
    private static readonly DateOnly SeptemberDue = new(2026, 9, 10);
    private static readonly DateOnly SecondOfSeptember = new(2026, 9, 2);

    private static Contract NewContract(ContractualFeePlan? plan = null) =>
        CollectionPlannerTests.Scenario.NewContract(plan: plan, start: September);

    private static (ContractAccount Account, IReadOnlyList<AccountMovement> Written, CollectionPreview Preview) PayEarly(
        Contract contract, ContractAccount account, DateOnly today)
    {
        var preview = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: September, today: today);
        var written = CollectionPlanner.MovementsToWrite(account, contract, preview);
        CollectionPlannerTests.Scenario.Apply(account, written);

        return (account, written, preview);
    }

    // Test 68, through the planner
    [Fact]
    public void AnEarlyPayment_WritesTheRentAndTheInstalment_AndDatesThePaymentOnTheDueDate()
    {
        var contract = NewContract(new ContractualFeePlan(90_000m, 2));
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        var (_, written, preview) = PayEarly(contract, account, SecondOfSeptember);

        Assert.True(preview.PeriodWasEarly);
        Assert.Equal(SecondOfSeptember, preview.ReceivedOn);
        Assert.Equal(590_000m, preview.LedgerFigure);
        Assert.Equal(
            [MovementKind.RentAccrual, MovementKind.ContractualFeeInstalment, MovementKind.Payment],
            written.Select(m => m.Kind));
        Assert.All(written, m => Assert.Equal(SeptemberDue, m.OccurredOn));
        Assert.DoesNotContain(written, m => m.Kind == MovementKind.RecargoCharged);
        Assert.Equal(0m, account.PeriodNet(September, SeptemberDue));
    }

    [Fact]
    public void WithNoPeriodNamed_TheMonthInCourseIsThePeriodOfAnEarlyPayment()
    {
        var contract = NewContract();
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        var preview = CollectionPlannerTests.Scenario.PlanExact(account, contract, today: SecondOfSeptember);

        Assert.Equal(September, preview.Period);
        Assert.True(preview.PeriodWasEarly);
        Assert.Null(preview.Refusal);
    }

    // Test 73
    [Fact]
    public void AVoidedEarlyPayment_IsPayableAgain_WritesNoSecondRentOrInstalment_AndLeavesThePeriodAtZero()
    {
        var contract = NewContract(new ContractualFeePlan(90_000m, 2));
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        var (_, written, preview) = PayEarly(contract, account, SecondOfSeptember);

        // Paid on the 2nd, voided on the 3rd.
        var thirdOfSeptember = new DateOnly(2026, 9, 3);
        var voiding = VoidingPlanner.Plan(
            account,
            VoidingPlannerTests.RecordOf(account, written, preview),
            VoidingPlannerTests.PairOf(preview).Recibos,
            "Wrong tenant",
            thirdOfSeptember,
            CollectionPlannerTests.Scenario.Terms,
            UserRole.Empleado,
            recordedOn: SecondOfSeptember);
        Assert.Null(voiding.Refusal);
        CollectionPlannerTests.Scenario.Apply(account, voiding.Corrections);
        Assert.False(account.HasLivePaymentFor(September));

        // Payable on the 4th, and the new payment is only a payment.
        var fourthOfSeptember = new DateOnly(2026, 9, 4);
        var again = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: September, today: fourthOfSeptember);
        var second = CollectionPlanner.MovementsToWrite(account, contract, again);

        Assert.Null(again.Refusal);
        Assert.True(again.PeriodWasEarly);
        Assert.Equal(590_000m, again.LedgerFigure);
        Assert.Equal([MovementKind.Payment], second.Select(m => m.Kind));
        Assert.Equal(-590_000m, second.Single().Amount);
        Assert.Equal(SeptemberDue, second.Single().OccurredOn);

        CollectionPlannerTests.Scenario.Apply(account, second);

        Assert.Single(account.Movements, m => m.Kind == MovementKind.RentAccrual);
        Assert.Single(account.Movements, m => m.Kind == MovementKind.ContractualFeeInstalment);
        Assert.Equal(2, account.Movements.Count(m => m.Kind == MovementKind.Payment));
        Assert.DoesNotContain(account.Movements, m => m.Kind == MovementKind.RecargoCharged);
        Assert.Equal(0m, account.PeriodNet(September, SeptemberDue));
        Assert.True(account.HasLivePaymentFor(September));
        Assert.Equal(0m, account.AmountOwed(SeptemberDue, CollectionPlannerTests.Scenario.Terms));
    }

    // Test 74 (pins existing behaviour, design D26: no new code is expected)
    [Fact]
    public void AnEarlyWrittenRent_IsFinal_AnAdjustmentConfirmedAfterwardsRewritesNothing_AndTheNextPeriodTakesTheNewCanon()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract(start: CollectionPlannerTests.Scenario.July);
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, new DateOnly(2026, 8, 31));
        var (_, written, _) = PayEarly(contract, account, SecondOfSeptember);
        Assert.Equal(500_000m, written.Single(m => m.Kind == MovementKind.RentAccrual).Amount);

        // An adjustment effective on September's due date or before, confirmed after the payment.
        CollectionPlannerTests.Scenario.Materialise(account, contract, new DateOnly(2026, 9, 30));
        ContractTestFactory.ConfirmAdjustment(contract, 500_000m, 10m, new DateOnly(2026, 9, 5));
        var movementsBefore = account.Movements.Count;
        var newCanon = AccrualSchedule.CanonFor(contract, new DateOnly(2026, 10, 1));
        Assert.Equal(550_000m, newCanon);

        CollectionPlannerTests.Scenario.Materialise(account, contract, new DateOnly(2026, 10, 31));

        // September's rent stays as written and nothing is generated for it.
        var september = account.Movements.Where(m => m.Kind == MovementKind.RentAccrual && m.Period == September).ToList();
        Assert.Equal(500_000m, Assert.Single(september).Amount);
        Assert.Equal(movementsBefore + 1, account.Movements.Count);

        // The next period accrues at the adjusted canon.
        var october = Assert.Single(account.Movements, m => m.Kind == MovementKind.RentAccrual && m.Period == CollectionPlannerTests.Scenario.October);
        Assert.Equal(newCanon, october.Amount);

        // The domain says the same on its own: a charged period takes no second rent.
        Assert.Throws<InvalidOperationException>(() => account.Append(
            AccountMovement.RentAccrual(Guid.NewGuid(), newCanon, September, SeptemberDue)));
    }
}
