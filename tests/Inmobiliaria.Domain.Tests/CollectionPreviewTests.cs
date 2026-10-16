using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// What the confirmation preview shows and when a commit is stale (cobranza-y-recibos spec Tests 19,
/// 20, 45, 47, 69; design D21 and section 2). The preview is a value: the commit rebuilds it from
/// fresh data and requires equality, so each of these tests changes one input and watches it differ.
/// </summary>
public class CollectionPreviewTests
{
    private static readonly DateOnly Today = CollectionPlannerTests.Scenario.Today;
    private static readonly DateOnly July = CollectionPlannerTests.Scenario.July;
    private static readonly DateOnly August = CollectionPlannerTests.Scenario.August;

    // Test 19
    [Fact]
    public void ThePreview_ShowsEveryFigureTheOperatorConfirms()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract();
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);

        // 7,000.50 is truncated on entry; the negative otros conceptos is a discount.
        var preview = CollectionPlannerTests.Scenario.PlanExact(
            account, contract, named: August, tasa: 7_000.50m, seguro: 3_000m, otros: -1_000m);

        Assert.Null(preview.Refusal);
        Assert.Equal(August, preview.Period);
        Assert.Equal(Today, preview.ReceivedOn);
        Assert.False(preview.PeriodWasEarly);

        // July 640,000 + August 330,000 + September 20,000 of recargo on 1,500,000 of rent.
        Assert.Equal(2_490_000m, preview.PreviousAmountOwed);
        Assert.Equal(830_000m, preview.LedgerFigure);
        Assert.Equal(1_660_000m, preview.ResultingAmountOwed);

        Assert.Equal(
            [ReciboLineKind.Rent, ReciboLineKind.Recargo, ReciboLineKind.TasaMunicipal, ReciboLineKind.Seguro, ReciboLineKind.OtrosConceptos],
            preview.Locatario!.Lines.Select(l => l.Kind));
        Assert.Equal(
            [500_000m, 330_000m, 7_000m, 3_000m, -1_000m],
            preview.Locatario.Lines.Select(l => l.Amount));
        Assert.Equal(839_000m, preview.Locatario.Total);

        // 8% of rent + recargo = 66,400.
        Assert.Equal(
            [ReciboLineKind.Rent, ReciboLineKind.Recargo, ReciboLineKind.TasaMunicipal, ReciboLineKind.Seguro, ReciboLineKind.OtrosConceptos, ReciboLineKind.HonorariosAdministracion],
            preview.Propietario!.Lines.Select(l => l.Kind));
        Assert.Equal(-66_400m, preview.Propietario.Lines.Last().Amount);
        Assert.Equal(772_600m, preview.Propietario.Total);
    }

    // Test 47
    [Fact]
    public void TheReciboTotal_AndTheLedgerFigure_DifferByTheTypedLines_AndAreNamedApart()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract();
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);

        var preview = CollectionPlannerTests.Scenario.PlanExact(
            account, contract, named: August, tasa: 7_000m, seguro: 3_000m, otros: -1_000m);

        Assert.Equal(830_000m, preview.LedgerFigure);
        Assert.Equal(839_000m, preview.Locatario!.Total);
        Assert.Equal(7_000m + 3_000m - 1_000m, preview.Locatario.Total - preview.LedgerFigure);

        // Two figures, two names: the ledger figure is a preview member, the paper's total belongs to the draft.
        Assert.Contains(typeof(CollectionPreview).GetProperties(), p => p.Name == nameof(CollectionPreview.LedgerFigure));
        Assert.DoesNotContain(typeof(CollectionPreview).GetProperties(), p => p.Name == "Total");
        Assert.Contains(typeof(ReciboDraft).GetProperties(), p => p.Name == nameof(ReciboDraft.Total));
    }

    // Test 47: with no typed lines the two figures coincide on the locatario recibo.
    [Fact]
    public void WithNoTypedLines_TheLocatarioTotal_IsTheLedgerFigure()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract();
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);

        var preview = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        Assert.Equal(preview.LedgerFigure, preview.Locatario!.Total);
    }

    // Test 45
    [Fact]
    public void ARateNeverEntered_CannotBeCommitted_AndThePreviewSaysSo()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract(rate: null);
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);
        var movementsBefore = account.Movements.Count;

        var preview = CollectionPlannerTests.Scenario.Plan(account, contract, handed: 830_000m, named: August);

        Assert.Equal(CollectionRefusal.RateNeverEntered, preview.Refusal);
        Assert.False(preview.CanCommit);
        Assert.Null(preview.Propietario);
        Assert.Equal(CollectionRefusal.RateNeverEntered, CollectionPlanner.Confirm(preview, preview));
        Assert.Throws<InvalidOperationException>(() => CollectionPlanner.MovementsToWrite(account, contract, preview));
        Assert.Equal(movementsBefore, account.Movements.Count);
        Assert.False(string.IsNullOrWhiteSpace(CollectionRefusal.RateNeverEntered.OperatorText()));
    }

    // Test 69
    [Fact]
    public void AZeroRate_IsCollected_ThePreviewWarns_AndTheHonorariosLineIsZero()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract(rate: 0m);
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);

        var preview = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        Assert.Null(preview.Refusal);
        Assert.True(preview.CanCommit);
        Assert.Contains(CollectionWarning.ZeroAdministrationRate, preview.Warnings);

        var honorarios = preview.Propietario!.Lines.Single(l => l.Kind == ReciboLineKind.HonorariosAdministracion);
        Assert.Equal(0m, honorarios.Amount);
        Assert.False(decimal.IsNegative(honorarios.Amount));
    }

    [Fact]
    public void ANonZeroRate_CarriesNoWarning()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract(rate: 8m);
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);

        var preview = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        Assert.Empty(preview.Warnings);
    }

    // Test 20
    [Fact]
    public void TwoPreviewsOfTheSameInputs_AreEqualAsValues()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract(rate: 0m);
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);

        var first = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August, tasa: 5_000m);
        var second = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August, tasa: 5_000m);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Null(CollectionPlanner.Confirm(first, second));
    }

    // Test 20
    [Fact]
    public void AnotherPaymentOfThisPeriod_MakesThePreviewStale()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract();
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);
        var shown = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        // Another operator pays August between the preview and the commit.
        var other = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);
        CollectionPlannerTests.Scenario.Apply(account, CollectionPlanner.MovementsToWrite(account, contract, other));
        var fresh = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        Assert.Equal(CollectionRefusal.PreviewStale, CollectionPlanner.Confirm(shown, fresh));
    }

    // Test 20
    [Fact]
    public void AnotherPaymentOfADifferentPeriod_MovesThePreviousAmountOwed_AndMakesThePreviewStale()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract();
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);
        var shown = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        var other = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: July);
        CollectionPlannerTests.Scenario.Apply(account, CollectionPlanner.MovementsToWrite(account, contract, other));
        var fresh = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        Assert.Null(fresh.Refusal);
        Assert.NotEqual(shown.PreviousAmountOwed, fresh.PreviousAmountOwed);
        Assert.Equal(shown.LedgerFigure, fresh.LedgerFigure);
        Assert.Equal(CollectionRefusal.PreviewStale, CollectionPlanner.Confirm(shown, fresh));
    }

    // Test 20
    [Fact]
    public void TheDayTurningOver_MovesTheResolvedToday_AndMakesThePreviewStale()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract();
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today.AddDays(1));
        var shown = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        // The same named received date, one day later on the clock.
        var fresh = CollectionPlannerTests.Scenario.PlanExact(
            account, contract, named: August, received: Today, today: Today.AddDays(1));

        Assert.Equal(Today, shown.ResolvedToday);
        Assert.Equal(Today.AddDays(1), fresh.ResolvedToday);
        Assert.Equal(CollectionRefusal.PreviewStale, CollectionPlanner.Confirm(shown, fresh));
    }

    // Test 20
    [Fact]
    public void ARateChange_MovesThePropietarioLine_AndMakesThePreviewStale()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract(rate: 8m);
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);
        var shown = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        var changed = CollectionPlannerTests.Scenario.NewContract(rate: 10m);
        var changedAccount = CollectionPlannerTests.Scenario.AccountFor(changed, Today);
        var fresh = CollectionPlannerTests.Scenario.PlanExact(changedAccount, changed, named: August);

        // Same figures, only the honorarios line moved (rate 10 against 8 on 830,000).
        Assert.Equal(shown.LedgerFigure, fresh.LedgerFigure);
        Assert.Equal(shown.Locatario, fresh.Locatario);
        Assert.NotEqual(shown.Propietario, fresh.Propietario);
        Assert.Equal(CollectionRefusal.PreviewStale, CollectionPlanner.Confirm(shown, fresh));
    }

    // Test 20: an unchanged, committable preview confirms.
    [Fact]
    public void AnUnchangedPreview_Confirms()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract();
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);
        var shown = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);
        var fresh = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);

        Assert.Null(CollectionPlanner.Confirm(shown, fresh));
    }

    [Fact]
    public void EveryRefusal_HasOperatorText()
    {
        foreach (var refusal in Enum.GetValues<CollectionRefusal>())
        {
            Assert.False(string.IsNullOrWhiteSpace(refusal.OperatorText()), refusal.ToString());
        }
    }
}
