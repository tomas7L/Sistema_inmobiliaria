using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using static Inmobiliaria.Domain.Tests.CollectionPlannerTests;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirements 19 and 20, Tests 52, 63 to 66, design §3.6: paying a period that carries a late
/// adjustment's difference. September owes rent of 575,000, due on the 10th, and a 300,000 charge; the rate is 8%.
/// </summary>
public class AdjustmentDifferenceCollectionTests
{
    private static readonly DateOnly September = Scenario.September;
    private static readonly DateOnly DueOn = new(2026, 9, 10);

    private static (Contract Contract, ContractAccount Account) WithCharge(Contract? contract = null)
    {
        contract ??= Scenario.NewContract(rent: 575_000m);
        var account = Scenario.AccountFor(contract, DueOn);
        account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 300_000m, September, DueOn));
        return (contract, account);
    }

    private static decimal LineOf(ReciboDraft draft, ReciboLineKind kind) => draft.Lines.Single(l => l.Kind == kind).Amount;

    // ---- Test 65 --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Test65_BothRecibosCarryTheLine_AndTheFeeFollows()
    {
        var (contract, account) = WithCharge();

        var preview = Scenario.PlanExact(account, contract, named: September, received: DueOn, today: DueOn);

        Assert.Null(preview.Refusal);
        Assert.Equal(875_000m, preview.LedgerFigure);
        Assert.Equal(575_000m, LineOf(preview.Locatario!, ReciboLineKind.Rent));
        Assert.Equal(300_000m, LineOf(preview.Locatario!, ReciboLineKind.AdjustmentDifference));
        Assert.Equal(300_000m, LineOf(preview.Propietario!, ReciboLineKind.AdjustmentDifference));
        Assert.Equal(-70_000m, LineOf(preview.Propietario!, ReciboLineKind.HonorariosAdministracion));
        Assert.DoesNotContain(preview.Locatario!.Lines, l => l.Kind == ReciboLineKind.Recargo);
    }

    [Fact]
    public void Test65_TheInstalment_StaysOutOfTheFeeBase()
    {
        var contract = Scenario.NewContract(rent: 575_000m, plan: new ContractualFeePlan(100_000m, 4));
        var (_, account) = WithCharge(contract);

        var preview = Scenario.PlanExact(account, contract, named: September, received: DueOn, today: DueOn);

        Assert.Equal(100_000m, LineOf(preview.Locatario!, ReciboLineKind.ContractualFeeInstalment));
        Assert.Equal(975_000m, preview.LedgerFigure);
        Assert.Equal(-70_000m, LineOf(preview.Propietario!, ReciboLineKind.HonorariosAdministracion));
    }

    [Fact]
    public void ThePreview_NamesTheMonthsTheChargeCovers_AndTheyArePartOfWhatIsConfirmed()
    {
        // The July adjustment, confirmed late and applying from September, charged its difference on September.
        var contract = Scenario.NewContract(rent: 575_000m);
        var account = Scenario.AccountFor(contract, DueOn);
        var charge = AccountMovement.AdjustmentDifference(Guid.NewGuid(), 150_000m, September, DueOn);
        account.Append(charge);
        var indexId = Guid.NewGuid();
        var proposal = new AdjustmentProposal(
            500_000m, [new RentAdjustmentIndexValue(indexId, indexId, new Indices.IndexPeriod(2026, 1), 100m, new Indices.IndexPeriod(2026, 6), 115m, 15m)],
            CombinationRule.Single, 15m, Scenario.July);
        contract.ConfirmAdjustment(Guid.NewGuid(), proposal, DateTimeOffset.UtcNow, Guid.NewGuid(),
            appliesFrom: September, differenceAmount: 150_000m, differenceMovementId: charge.Id);

        var preview = Scenario.PlanExact(account, contract, named: September, received: DueOn, today: DueOn);

        Assert.Equal([Scenario.July, Scenario.August], preview.DifferenceMonths);
        Assert.NotEqual(preview, preview with { DifferenceMonths = [Scenario.July] });
    }

    // ---- Test 66 --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Test66_HandingTheRentWithoutTheCharge_IsRefusedAsNotTheTotal()
    {
        var (contract, account) = WithCharge();

        var preview = Scenario.Plan(account, contract, 575_000m, named: September, received: DueOn, today: DueOn);

        Assert.Equal(CollectionRefusal.AmountNotTotal, preview.Refusal);
    }

    // ---- Test 52, when charged ------------------------------------------------------------------------------------------

    [Fact]
    public void Test52_PaidLate_TheRecargoChargedIsOnTheRentAlone()
    {
        var (contract, account) = WithCharge();
        var late = new DateOnly(2026, 9, 20);

        var preview = Scenario.PlanExact(account, contract, named: September, received: late, today: late);

        var expected = decimal.Truncate(RecargoMath.For(575_000m, DueOn, late, Scenario.Terms));
        Assert.Equal(expected, LineOf(preview.Locatario!, ReciboLineKind.Recargo));
        Assert.Equal(875_000m + expected, preview.LedgerFigure);
    }

    // ---- Test 63 --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Test63_APeriodItsEndedContractNeverAccrued_IsPaidForTheChargeAlone_WithNoRentLine()
    {
        var contract = Scenario.NewContract(rent: 575_000m);
        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 8, 20));
        var account = Scenario.AccountFor(contract, DueOn);
        account.Append(AccountMovement.AdjustmentDifference(Guid.NewGuid(), 300_000m, September, DueOn));

        var preview = Scenario.PlanExact(account, contract, named: September, received: new DateOnly(2026, 9, 12));

        Assert.Null(preview.Refusal);
        Assert.Equal(300_000m, preview.LedgerFigure);
        Assert.DoesNotContain(preview.Locatario!.Lines, l => l.Kind == ReciboLineKind.Rent);
        Assert.Equal(300_000m, LineOf(preview.Locatario!, ReciboLineKind.AdjustmentDifference));

        var written = CollectionPlanner.MovementsToWrite(account, contract, preview);
        Assert.DoesNotContain(written, m => m.Kind == MovementKind.RentAccrual);
        Assert.Equal(-300_000m, Assert.Single(written, m => m.Kind == MovementKind.Payment).Amount);
    }

    // ---- Test 64 --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Test64_VoidingThePayment_LeavesTheRentAndTheChargeOwed()
    {
        var (contract, account) = WithCharge();
        var preview = Scenario.PlanExact(account, contract, named: September, received: DueOn, today: DueOn);
        var written = CollectionPlanner.MovementsToWrite(account, contract, preview);
        Scenario.Apply(account, written);
        Assert.Equal(0m, account.PeriodNet(September));

        var voiding = VoidingPlanner.Plan(
            account,
            VoidingPlannerTests.RecordOf(account, written, preview),
            VoidingPlannerTests.PairOf(preview).Recibos,
            "Cobrado por error",
            DueOn.AddDays(1),
            Scenario.Terms,
            UserRole.Empleado,
            DueOn);
        Scenario.Apply(account, voiding.Corrections);

        Assert.Equal(875_000m, account.PeriodNet(September));
        Assert.Equal(300_000m, account.DifferenceNet(September));
    }
}
