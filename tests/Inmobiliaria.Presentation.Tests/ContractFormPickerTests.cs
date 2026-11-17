using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// The contract form's pickers, as on the designer's canvas "Nuevo contrato" (task 12.3d): the operator types part of a name
/// or an address and picks from what matches, accents and case aside; each row then has "Quitar", and each tab counts the
/// errors it holds.
/// </summary>
public class ContractFormPickerTests
{
    private static readonly PartyListing Demaria = new(Guid.NewGuid(), "Nicolás", "Demaría", "30123456", null, null, null, null)
    {
        ActiveRoles = [PartyRole.Lessor],
    };

    private static readonly PartyListing Ocampo = new(Guid.NewGuid(), "Martín", "Ocampo", "28111222", null, "m@x.com", null, null);

    private static readonly PartyListing Molina = new(Guid.NewGuid(), "Sofía", "Molina", null, "27222333444", null, null, null);

    private static readonly UnitListing Alem = new(
        Guid.NewGuid(), UnitType.Property, new Address("Alem", "1258", "Sunchales", "Santa Fe", "2322"), null);

    private static readonly UnitListing Rivadavia = new(
        Guid.NewGuid(), UnitType.Property, new Address("Rivadavia", "640", "Sunchales", "Santa Fe", "2322"),
        new UnitCoverage(ContractStatus.Active, "Sosa, Ramiro", null));

    private static async Task<ContractFormViewModel> LoadedAsync(FakeContractRegistry? port = null)
    {
        var viewModel = new ContractFormViewModel(
            port ?? new FakeContractRegistry(), false, new FakeClock(new DateOnly(2026, 11, 15)), new FakeIndexRegistry(),
            new FakePartyRegistry { Persons = [Demaria, Ocampo, Molina] }, new FakeUnitRegistry { Units = [Alem, Rivadavia] });
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task TypingPartOfAName_SuggestsWhoMatches_AccentsAndCaseAside()
    {
        var viewModel = await LoadedAsync();

        viewModel.TenantPicker.Query = "demaria";

        var option = Assert.Single(viewModel.TenantPicker.Suggestions);
        Assert.Equal(("Demaría, Nicolás", "Propietario"), (option.Text, option.Tag));
    }

    [Fact]
    public async Task ADniOrCuil_FindsThePerson()
    {
        var viewModel = await LoadedAsync();

        viewModel.TenantPicker.Query = "28111";
        Assert.Equal(Ocampo, Assert.Single(viewModel.TenantPicker.Suggestions).Item);

        viewModel.TenantPicker.Query = "27222333";
        Assert.Equal(Molina, Assert.Single(viewModel.TenantPicker.Suggestions).Item);
    }

    [Fact]
    public async Task OneLetter_SuggestsNothing_AndNothingMatching_SaysSo()
    {
        var viewModel = await LoadedAsync();

        viewModel.TenantPicker.Query = "d";
        Assert.Empty(viewModel.TenantPicker.Suggestions);
        Assert.False(viewModel.TenantPicker.HasNoMatch);

        viewModel.TenantPicker.Query = "zz";
        Assert.Empty(viewModel.TenantPicker.Suggestions);
        Assert.True(viewModel.TenantPicker.HasNoMatch);
    }

    [Fact]
    public async Task PickingATenant_SetsIt_AndEmptiesThePicker()
    {
        var viewModel = await LoadedAsync();
        viewModel.TenantPicker.Query = "ocampo";

        await viewModel.TenantPicker.PickCommand.ExecuteAsync(viewModel.TenantPicker.Suggestions[0]);

        Assert.Equal(Ocampo, viewModel.Tenant);
        Assert.Equal(string.Empty, viewModel.TenantPicker.Query);
        Assert.Empty(viewModel.TenantPicker.Suggestions);
    }

    [Fact]
    public async Task PickingALessorAndACodebtor_AddsThemAsRows()
    {
        var viewModel = await LoadedAsync();

        viewModel.LessorPicker.Query = "demar";
        await viewModel.LessorPicker.PickCommand.ExecuteAsync(viewModel.LessorPicker.Suggestions[0]);
        viewModel.CodebtorPicker.Query = "molina";
        await viewModel.CodebtorPicker.PickCommand.ExecuteAsync(viewModel.CodebtorPicker.Suggestions[0]);

        Assert.Equal([Demaria], viewModel.Lessors);
        Assert.Equal([Molina], viewModel.Codebtors);
    }

    [Fact]
    public async Task AUnit_IsFoundByItsAddress_AndSaysWhetherItIsLet()
    {
        var viewModel = await LoadedAsync();

        viewModel.UnitPicker.Query = "riva";
        var let = Assert.Single(viewModel.UnitPicker.Suggestions);
        viewModel.UnitPicker.Query = "alem";
        var free = Assert.Single(viewModel.UnitPicker.Suggestions);

        Assert.Equal("Alquilada a Sosa, Ramiro", let.Tag);
        Assert.Equal("Disponible", free.Tag);
        await viewModel.UnitPicker.PickCommand.ExecuteAsync(free);
        Assert.Equal(Alem, Assert.Single(viewModel.Units).Unit);
    }

    [Fact]
    public async Task EachRow_CanBeRemoved_AndTheTenantCleared()
    {
        var viewModel = await LoadedAsync();
        viewModel.Tenant = Ocampo;
        await viewModel.AddLessorCommand.ExecuteAsync(Demaria);
        viewModel.Codebtors.Add(Molina);
        viewModel.Units.Add(new ContractFormUnit(Alem));

        viewModel.ClearTenantCommand.Execute(null);
        viewModel.RemoveLessorCommand.Execute(Demaria);
        viewModel.RemoveCodebtorCommand.Execute(Molina);
        viewModel.RemoveUnitCommand.Execute(viewModel.Units[0]);

        Assert.Null(viewModel.Tenant);
        Assert.Empty(viewModel.Lessors);
        Assert.Empty(viewModel.Codebtors);
        Assert.Empty(viewModel.Units);
    }

    /// <summary>A row's error names a position, so removing a row withdraws the errors instead of leaving them on the wrong rows.</summary>
    [Fact]
    public async Task RemovingARow_WithdrawsTheErrors()
    {
        var viewModel = await LoadedAsync();
        await viewModel.AddLessorCommand.ExecuteAsync(Demaria);
        await viewModel.AddLessorCommand.ExecuteAsync(Demaria);
        Assert.NotNull(viewModel.Errors.ForRow(ContractField.Lessors, 1));

        viewModel.RemoveLessorCommand.Execute(viewModel.Lessors[0]);

        Assert.Equal(0, viewModel.Errors.Count);
    }

    /// <summary>Each tab counts the errors it holds, row errors included, so the operator sees where to look.</summary>
    [Fact]
    public async Task EachTab_CountsItsErrors()
    {
        var viewModel = await LoadedAsync();
        await viewModel.AddLessorCommand.ExecuteAsync(Demaria);
        await viewModel.AddLessorCommand.ExecuteAsync(Demaria);
        viewModel.MonthlyRentText = "abc";

        await viewModel.SaveCommand.ExecuteAsync(null);

        // Partes: no tenant, the repeated lessor. Propiedad: no unit. Fechas e importes: no dates (start and end), the rent.
        Assert.Equal(
            (2, 1, 3, 0),
            (viewModel.Errors.CountIn(ContractFormSection.Parties), viewModel.Errors.CountIn(ContractFormSection.Units),
                viewModel.Errors.CountIn(ContractFormSection.DatesAndAmounts), viewModel.Errors.CountIn(ContractFormSection.Clause)));
    }

    [Fact]
    public async Task WithoutTheRegistries_ThePickersOfferNothing()
    {
        var viewModel = new ContractFormViewModel(
            new FakeContractRegistry(), false, new FakeClock(new DateOnly(2026, 11, 15)), new FakeIndexRegistry());
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.TenantPicker.Query = "ocampo";

        Assert.Empty(viewModel.TenantPicker.Suggestions);
    }
}
