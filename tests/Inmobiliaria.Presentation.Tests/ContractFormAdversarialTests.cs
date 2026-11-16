using System.Globalization;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>From the independent verification of slice S3, on the form: what an operator can type must not throw out of the command.</summary>
public class ContractFormAdversarialTests
{
    private static readonly PartyListing Juan = new(Guid.NewGuid(), "Juan", "Pérez", "30123456", null, null, null, null);
    private static readonly PartyListing Ana = new(Guid.NewGuid(), "Ana", "Gómez", "28111222", null, null, null, null);

    private static UnitListing Unit(string number = "1258") =>
        new(Guid.NewGuid(), UnitType.Property, new Address("Alem", number, "Sunchales", "Santa Fe", "2322"), null);

    private static async Task<ContractFormViewModel> FilledAsync(FakeContractRegistry port, bool admin = false)
    {
        var viewModel = new ContractFormViewModel(port, admin, new FakeClock(new DateOnly(2026, 11, 15))) { Tenant = Juan };
        await viewModel.AddLessorCommand.ExecuteAsync(Ana);
        viewModel.Units.Add(new ContractFormUnit(Unit()));
        viewModel.StartDate = new DateOnly(2026, 12, 1);
        viewModel.NominalEndDate = new DateOnly(2028, 11, 30);
        viewModel.MonthlyRentText = "450.000";
        viewModel.HonorariosText = "8";
        return viewModel;
    }

    /// <summary>29-digit amounts typed per unit: the sum overflows decimal.</summary>
    [Fact]
    public async Task HugeTypedAmounts_DoNotThrowOutOfTheCommand()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.Units.Add(new ContractFormUnit(Unit("40")));
        viewModel.SharesAsAmounts = true;
        viewModel.Units[0].ShareText = "79228162514264337593543950335";
        viewModel.Units[1].ShareText = "79228162514264337593543950335";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.Errors[ContractField.Shares]);
        Assert.Empty(port.Created);
    }

    /// <summary>Too large for a decimal: the form refuses it itself and the port is never called.</summary>
    [Fact]
    public async Task ACanonTooLargeToBeANumber_IsRefusedByTheForm()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.MonthlyRentText = "99999999999999999999999999999999999";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.RentNotWholePositive.OperatorText(), viewModel.Errors[ContractField.Rent]);
        Assert.Empty(port.Created);
    }

    /// <summary>
    /// Odd canons that parse are judged by the form itself (spec Decision 44): beyond the bound, with cents, zero or negative,
    /// each is refused beside the canon and the port is never called.
    /// </summary>
    [Theory]
    [InlineData("79228162514264337593543950335")]
    [InlineData("1,123456789012345678901234567890123")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("1.000.000.000.000")]
    public async Task OddCanonsThatParse_AreRefusedByTheForm(string typed)
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.MonthlyRentText = typed;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.RentNotWholePositive.OperatorText(), viewModel.Errors[ContractField.Rent]);
        Assert.Empty(port.Created);
    }

    /// <summary>The largest whole canon the column holds reaches the port as typed.</summary>
    [Fact]
    public async Task TheLargestWholeCanon_ReachesThePortAsTyped()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.MonthlyRentText = "999.999.999.999";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(999_999_999_999m, Assert.Single(port.Created).Draft.MonthlyRent);
    }

    [Fact]
    public async Task AnEmpleado_HoldingADateInTheField_NeverSendsIt()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.CutoverDate = new DateOnly(2026, 1, 1);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(port.Created).Draft.CutoverDate);
        Assert.False(viewModel.IsCutoverOffered);
    }

    [Fact]
    public void TheEmpleadosRefusal_SaysAnAdminHasToLoadIt_AndNoTextSaysGarante()
    {
        Assert.Contains("Admin", ContractRefusal.StartBeforeCurrentMonth.OperatorText(), StringComparison.Ordinal);
        foreach (var refusal in Enum.GetValues<ContractRefusal>())
        {
            Assert.DoesNotContain("garante", refusal.OperatorText().ToLowerInvariant(), StringComparison.Ordinal);
        }
    }
}
