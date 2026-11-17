using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// What the contract form's screen shows beside its fields, as on the designer's canvas "Nuevo contrato" (task 12.3d): the
/// fixed-rent choice, the indices as switches, the duration, the offset's suggestion and the saved notice.
/// </summary>
public class ContractFormScreenTests
{
    private static readonly Guid Ipc = Guid.NewGuid();
    private static readonly Guid Icl = Guid.NewGuid();

    private static async Task<ContractFormViewModel> LoadedAsync(FakeContractRegistry? port = null)
    {
        var catalog = new FakeIndexRegistry { Indices = [new IndexListing(Ipc, "IPC", null, []), new IndexListing(Icl, "ICL", null, [])] };
        var viewModel = new ContractFormViewModel(port ?? new FakeContractRegistry(), false, new FakeClock(new DateOnly(2026, 11, 15)), catalog);
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task AFixedRent_IsTheStart_AndChoosingItAgainForgetsTheClause()
    {
        var viewModel = await LoadedAsync();
        Assert.False(viewModel.HasClause);

        viewModel.HasClause = true;
        viewModel.ClauseIndexChoices[0].IsChosen = true;
        viewModel.ClauseOffsetText = "2";
        viewModel.HasClause = false;

        Assert.Empty(viewModel.ClauseIndexIds);
        Assert.Equal((string.Empty, string.Empty), (viewModel.ClauseIntervalText, viewModel.ClauseOffsetText));
        Assert.All(viewModel.ClauseIndexChoices, choice => Assert.False(choice.IsChosen));
    }

    [Fact]
    public async Task ChoosingAClause_StartsEverySixMonths()
    {
        var viewModel = await LoadedAsync();

        viewModel.HasClause = true;

        Assert.Equal("6", viewModel.ClauseIntervalText);
    }

    [Fact]
    public async Task EachIndex_IsASwitch_ThatAddsAndRemovesIt()
    {
        var viewModel = await LoadedAsync();
        viewModel.HasClause = true;

        viewModel.ClauseIndexChoices.Single(c => c.Option.Id == Icl).IsChosen = true;
        viewModel.ClauseIndexChoices.Single(c => c.Option.Id == Ipc).IsChosen = true;
        Assert.Equal([Icl, Ipc], viewModel.ClauseIndexIds);

        viewModel.ClauseIndexChoices.Single(c => c.Option.Id == Icl).IsChosen = false;
        Assert.Equal([Ipc], viewModel.ClauseIndexIds);
    }

    [Fact]
    public async Task TheOffset_SaysWhatTheIndicesSuggest()
    {
        var viewModel = await LoadedAsync();
        viewModel.HasClause = true;
        Assert.Equal("Se sugiere según los índices elegidos: ICL 0, IPC 2, y 3 si lleva RIPTE.", viewModel.ClauseOffsetHint);

        viewModel.ClauseIndexChoices.Single(c => c.Option.Id == Ipc).IsChosen = true;

        Assert.Equal(
            "Sugerido para IPC: 2. Son los últimos valores que normalmente ya están publicados al vencer.", viewModel.ClauseOffsetHint);
    }

    [Fact]
    public async Task TheDuration_ShowsOnceBothDatesAreThere()
    {
        var viewModel = await LoadedAsync();
        Assert.Equal("Escriba las dos fechas para ver la duración.", viewModel.DurationText);

        viewModel.StartDate = new DateOnly(2026, 12, 1);
        viewModel.NominalEndDate = new DateOnly(2028, 11, 30);

        Assert.Equal("Duración: 24 meses", viewModel.DurationText);
    }

    [Theory]
    [InlineData("8", "Se creó el contrato junto con su cuenta, vacía. Ya se puede cobrar desde la caja.")]
    [InlineData("", "Se creó el contrato junto con su cuenta, vacía. Ya se puede cobrar desde la caja, apenas se carguen los honorarios de administración.")]
    public async Task Saving_SaysTheAccountWasCreated_AndWhetherTheDeskCanCollect(string rate, string text)
    {
        var viewModel = await LoadedAsync();
        viewModel.Tenant = new PartyListing(Guid.NewGuid(), "Martín", "Ocampo", null, null, "m@x.com", null, null);
        await viewModel.AddLessorCommand.ExecuteAsync(new PartyListing(Guid.NewGuid(), "Elena", "Ruiz", null, null, null, null, null));
        viewModel.Units.Add(new ContractFormUnit(
            new UnitListing(Guid.NewGuid(), UnitType.Property, new Address("Alem", "1258", "Sunchales", "Santa Fe", "2322"), null)));
        viewModel.StartDate = new DateOnly(2026, 12, 1);
        viewModel.NominalEndDate = new DateOnly(2028, 11, 30);
        viewModel.MonthlyRentText = "450.000";
        viewModel.HonorariosText = rate;

        await viewModel.SaveCommand.ExecuteAsync(null);
        if (viewModel.HasWarnings)
        {
            await viewModel.AcceptWarningsCommand.ExecuteAsync(null);
        }

        Assert.NotNull(viewModel.Created);
        Assert.Equal(text, viewModel.SavedText);
    }

    /// <summary>A date typed short is read, and shown back in full.</summary>
    [Fact]
    public async Task ADateTypedShort_IsReadAndShownInFull()
    {
        var viewModel = await LoadedAsync();

        viewModel.StartDateText = "1226";
        viewModel.NominalEndDateText = "11/28";

        Assert.Equal((new DateOnly(2026, 12, 1), new DateOnly(2028, 11, 30)), (viewModel.StartDate, viewModel.NominalEndDate));
        Assert.Equal(("01/12/2026", "30/11/2028"), (viewModel.StartDateText, viewModel.NominalEndDateText));
    }

    /// <summary>
    /// Text that is no date stays as typed and is refused beside its field, never emptied without a word (Tomás, 2026-11-17).
    /// </summary>
    [Fact]
    public async Task TextThatIsNoDate_StaysAndIsRefusedBesideItsField()
    {
        var viewModel = await LoadedAsync();
        viewModel.StartDateText = "1/12/26";

        viewModel.StartDateText = "1122026";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("1122026", viewModel.StartDateText);
        Assert.Null(viewModel.StartDate);
        Assert.Equal("No se entiende la fecha: escríbala con día, mes y año, por ejemplo 1/12/26.", viewModel.Errors[ContractField.StartDate]);
    }

    /// <summary>A date set by the form itself shows in its field too.</summary>
    [Fact]
    public async Task ADateSetElsewhere_ShowsInItsField()
    {
        var viewModel = await LoadedAsync();

        viewModel.CutoverDate = new DateOnly(2026, 11, 9);

        Assert.Equal("09/11/2026", viewModel.CutoverDateText);
    }

    /// <summary>A date that is none is said on leaving its field, and the word goes once it is right (Tomás, 2026-11-17).</summary>
    [Fact]
    public async Task ADateThatIsNone_IsSaidOnLeavingItsField_AndGoesOnceRight()
    {
        var viewModel = await LoadedAsync();

        viewModel.StartDateText = "1122026";
        Assert.Equal(ContractRefusal.DateNotUnderstood.OperatorText(), viewModel.Errors[ContractField.StartDate]);

        viewModel.StartDateText = "1226";
        Assert.Null(viewModel.Errors[ContractField.StartDate]);
    }

    /// <summary>
    /// The cutover shows only where it serves, as on the canvas: an Administrador's contract starting before this month. A
    /// cutover typed and then hidden is forgotten, so nothing unseen is sent.
    /// </summary>
    [Fact]
    public async Task TheCutover_ShowsOnlyForAnAdministradorsContractStartedBeforeThisMonth()
    {
        var catalog = new FakeIndexRegistry();
        var admin = new ContractFormViewModel(new FakeContractRegistry(), true, new FakeClock(new DateOnly(2026, 11, 15)), catalog);
        var employee = new ContractFormViewModel(new FakeContractRegistry(), false, new FakeClock(new DateOnly(2026, 11, 15)), catalog);

        admin.StartDateText = "1026";
        employee.StartDateText = "1026";
        Assert.Equal((true, false), (admin.IsCutoverShown, employee.IsCutoverShown));

        admin.CutoverDateText = "9/11/26";
        admin.StartDateText = "1226";

        Assert.False(admin.IsCutoverShown);
        Assert.Null(admin.CutoverDate);
        Assert.Equal(string.Empty, admin.CutoverDateText);
    }

    /// <summary>The fee plan stays behind "+ Agregar honorarios contractuales", as on the canvas; taking it away forgets it.</summary>
    [Fact]
    public async Task TheFeePlan_IsAdded_AndTakingItAwayForgetsIt()
    {
        var viewModel = await LoadedAsync();
        Assert.False(viewModel.HasFeePlan);

        viewModel.AddFeePlanCommand.Execute(null);
        Assert.True(viewModel.HasFeePlan);
        viewModel.FeePlanAmountText = "20000";
        viewModel.FeePlanInstalmentsText = "3";
        viewModel.RemoveFeePlanCommand.Execute(null);

        Assert.False(viewModel.HasFeePlan);

        Assert.Equal((string.Empty, string.Empty), (viewModel.FeePlanAmountText, viewModel.FeePlanInstalmentsText));
    }

    /// <summary>A plan already typed shows its fields, so its error is never under something hidden.</summary>
    [Fact]
    public async Task APlanTyped_ShowsItsFields()
    {
        var viewModel = await LoadedAsync();

        viewModel.FeePlanAmountText = "20000";

        Assert.True(viewModel.HasFeePlan);
    }
}
