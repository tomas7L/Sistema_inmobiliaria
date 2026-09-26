using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Forces <c>OnModelCreating</c> to run and validate — without opening any connection — so a
/// broken EF mapping (an unresolvable navigation, a conflicting FK, a missing key) fails the
/// `core` CI job the same way a prior slice in this project once passed `dotnet build` and
/// broke CI, because `dotnet build` alone never builds the EF model and this repository's CI
/// (`.github/workflows/ci.yml`) never invokes `dotnet ef`. Reading <see cref="DbContext.Model"/>
/// is what triggers EF's model-building and validation conventions; the connection string below
/// is syntactically valid but nothing ever calls <c>Open()</c> on it.
/// </summary>
public sealed class EfModelValidationTests
{
    private const string NeverConnectedConnectionString =
        "Host=localhost;Database=x;Username=x;Password=x";

    [Fact]
    public void OnModelCreating_BuildsAndValidatesWithNoLiveDatabase()
    {
        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql(NeverConnectedConnectionString)
            .Options;

        using var context = new InmobiliariaDbContext(options);

        // Accessing Model is what forces OnModelCreating (and every convention/validation that
        // follows it) to run. No connection is ever opened; a broken mapping throws here.
        var model = context.Model;

        Assert.NotNull(model.FindEntityType(typeof(Inmobiliaria.Domain.Access.AppUser)));
        Assert.NotNull(model.FindEntityType(typeof(Inmobiliaria.Domain.Leasing.ContractDocument)));
        Assert.NotNull(model.FindEntityType(typeof(Inmobiliaria.Domain.Leasing.RentAdjustment)));
    }
}
