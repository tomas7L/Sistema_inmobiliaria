using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// The desk opened from a contract's "Ver cuenta" (canvas "Detalle del contrato"): it finds that contract's account by the
/// contract's short id, the handle its search already reads, and keeps only the account of that very contract.
/// </summary>
public class CashDeskContractTests
{
    private static AccountListing ListingOf(Guid contractId) =>
        new(Guid.NewGuid(), contractId, contractId.ToString("N")[..8], "Ocampo, Martín", "Ruiz, Elena", "Pellegrini 722, Sunchales",
            ContractStatus.Active, null, new AccountReadiness(true, false, true));

    [Fact]
    public async Task AContractsAccount_IsFoundByItsShortId_AndOnlyThatContractsIsTaken()
    {
        var kit = new CashDeskTestKit();
        var contractId = Guid.NewGuid();
        var mine = ListingOf(contractId);
        kit.Directory.Listings = [ListingOf(Guid.NewGuid()), mine];
        var desk = kit.Build();

        var found = await desk.FindContractAsync(contractId);

        Assert.Equal(mine, found);
        Assert.Equal([contractId.ToString("N")[..8]], kit.Directory.Searches);
    }

    [Fact]
    public async Task AContractTheDeskDoesNotShow_IsNotFound()
    {
        var kit = new CashDeskTestKit();
        kit.Directory.Listings = [ListingOf(Guid.NewGuid())];

        Assert.Null(await kit.Build().FindContractAsync(Guid.NewGuid()));
    }
}
