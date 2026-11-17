using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 6 and task 12.3d on the list's logic, with the port faked: the five state filters,
/// a cancelled contract apart from the ended ones, and the end and rent of each row.
/// </summary>
public class ContractsViewModelTests
{
    private static ContractListing Listing(string tenant, ContractStatus status, bool cancelled = false, DateOnly? actualEnd = null) => new(
        Guid.NewGuid(), tenant, "Ruiz, Elena", "Pellegrini 722", status, new DateOnly(2025, 3, 1), new DateOnly(2027, 2, 28), false, cancelled,
        428_025m, actualEnd);

    private static readonly ContractListing Vigente = Listing("Ocampo, Martín", ContractStatus.Active);
    private static readonly ContractListing EnRescision = Listing("Sosa, Ana", ContractStatus.PendingTermination);
    private static readonly ContractListing Finalizado = Listing("Pérez, Juan", ContractStatus.Ended, actualEnd: new DateOnly(2026, 10, 31));
    private static readonly ContractListing Cancelado = Listing("Molina, Sofía", ContractStatus.Ended, cancelled: true, actualEnd: new DateOnly(2026, 9, 1));

    private static async Task<ContractsViewModel> LoadedAsync(FakeContractRegistry? port = null)
    {
        port ??= new FakeContractRegistry();
        port.Contracts.AddRange([Vigente, EnRescision, Finalizado, Cancelado]);
        var viewModel = new ContractsViewModel(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task Todos_ListsEveryContract_CancelledIncluded()
    {
        var viewModel = await LoadedAsync();

        Assert.Equal(ContractListFilter.All, viewModel.Filter);
        Assert.Equal(4, viewModel.Contracts.Count);
        Assert.Equal("4 contratos", viewModel.CountText);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>Task 12.3d: Cancelados holds only the ones cancelled by mistake, and Finalizados never shows them.</summary>
    [Theory]
    [InlineData(ContractListFilter.Active, "Ocampo, Martín")]
    [InlineData(ContractListFilter.PendingTermination, "Sosa, Ana")]
    [InlineData(ContractListFilter.Ended, "Pérez, Juan")]
    [InlineData(ContractListFilter.Cancelled, "Molina, Sofía")]
    public async Task EachFilter_ListsOnlyItsContracts(ContractListFilter filter, string tenant)
    {
        var viewModel = await LoadedAsync();

        viewModel.Filter = filter;

        Assert.Equal(tenant, Assert.Single(viewModel.Contracts).Tenants);
        Assert.Equal("1 contrato", viewModel.CountText);
    }

    /// <summary>Changing the filter asks the database nothing: what was loaded is filtered on the screen.</summary>
    [Fact]
    public async Task ChangingTheFilter_AsksThePortNothing()
    {
        var port = new FakeContractRegistry();
        var viewModel = await LoadedAsync(port);

        viewModel.Filter = ContractListFilter.Cancelled;
        viewModel.Filter = ContractListFilter.All;

        Assert.Single(port.Lists);
        Assert.Equal(4, viewModel.Contracts.Count);
    }

    [Fact]
    public async Task AFilterWithNothing_IsEmpty()
    {
        var port = new FakeContractRegistry();
        port.Contracts.Add(Vigente);
        var viewModel = new ContractsViewModel(port);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.Filter = ContractListFilter.Cancelled;

        Assert.True(viewModel.IsEmpty);
        Assert.Equal("0 contratos", viewModel.CountText);
    }

    /// <summary>Task 12.3d: each row carries its end and its rent; an ended contract shows the day it really ended.</summary>
    [Fact]
    public async Task EachRow_ShowsItsEndAndRent()
    {
        var viewModel = await LoadedAsync();

        var vigente = viewModel.Contracts.Single(r => r.Id == Vigente.Id);
        var finalizado = viewModel.Contracts.Single(r => r.Id == Finalizado.Id);
        var cancelado = viewModel.Contracts.Single(r => r.Id == Cancelado.Id);

        Assert.Equal("28/02/2027", vigente.EndText);
        Assert.Equal("$ 428.025", vigente.RentText);
        Assert.Equal("31/10/2026", finalizado.EndText);
        Assert.Equal("Finalizado", finalizado.StatusText);
        Assert.Equal("Cancelado por error de carga", cancelado.StatusText);
    }

    /// <summary>The search goes to the port with every state, so the filter can then be changed without asking again.</summary>
    [Fact]
    public async Task Searching_AsksThePortForEveryState()
    {
        var port = new FakeContractRegistry();
        var viewModel = new ContractsViewModel(port) { SearchText = "ocampo", Filter = ContractListFilter.Ended };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(("ocampo", (ContractStatus?)null), Assert.Single(port.Lists));
    }

    /// <summary>"Abrir" puts the contract's detail in place of the list, read at once (canvas "Contratos" and "Detalle del contrato").</summary>
    [Fact]
    public async Task Opening_ShowsTheDetailOfThatContract_ReadAtOnce()
    {
        var port = new FakeContractRegistry { Detail = DetailOf(Vigente) };
        port.Contracts.Add(Vigente);
        var opened = new List<Guid>();
        var viewModel = new ContractsViewModel(port, id =>
        {
            opened.Add(id);
            return new ContractDetailViewModel(port, new FakeAdjustmentDesk(), id);
        });
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.OpenCommand.ExecuteAsync(viewModel.Contracts[0]);

        Assert.Equal([Vigente.Id], opened);
        Assert.True(viewModel.IsDetailOpen);
        Assert.Equal(Vigente.Id, viewModel.OpenDetail!.Detail!.Id);
    }

    /// <summary>"Volver a contratos" closes the detail and reads the list again, so a change made there shows.</summary>
    [Fact]
    public async Task GoingBack_ClosesTheDetail_AndReadsTheListAgain()
    {
        var port = new FakeContractRegistry { Detail = DetailOf(Vigente) };
        port.Contracts.Add(Vigente);
        var viewModel = new ContractsViewModel(port, id => new ContractDetailViewModel(port, new FakeAdjustmentDesk(), id));
        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.OpenCommand.ExecuteAsync(viewModel.Contracts[0]);
        port.Contracts.Add(EnRescision);

        await viewModel.BackCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDetailOpen);
        Assert.Null(viewModel.OpenDetail);
        Assert.Equal(2, viewModel.Contracts.Count);
    }

    /// <summary>Without a way to build the detail, nothing is offered to open.</summary>
    [Fact]
    public async Task WithoutADetail_OpeningIsNotOffered()
    {
        var viewModel = await LoadedAsync();

        Assert.False(viewModel.OpenCommand.CanExecute(viewModel.Contracts[0]));
    }

    private static ContractDetail DetailOf(ContractListing listing) => new(
        listing.Id, listing.Status, new DateOnly(2025, 3, 1), new DateOnly(2027, 2, 28), false, null, null, null, null, null, 428_025m, 10, 8m,
        null, [], [], null, []);
}
