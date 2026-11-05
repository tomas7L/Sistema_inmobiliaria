using System.Data.Common;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Tests 8 and 9 (Requirements 4 and 5) against real Postgres, as real Admin and Empleado
/// logins, with effects read back by raw SQL. Each test has its own database, because a list answers over every row.
/// All people and addresses are fictional.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UnitRegistryTests
{
    private static readonly UnitDraft AlemDraft = new("Alem", "1258", "Sunchales", "Santa Fe", "2322", "2", "B");

    private readonly PostgresFixture _fixture;

    public UnitRegistryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<DeskWorld> WorldAsync() => DeskWorld.CreateAsync(_fixture);

    private static UnitRegistry Registry(DeskWorld world) => new(world.NewContext());

    /// <summary>A registry on a context logged in as a real member of <paramref name="groupRole"/>.</summary>
    private static async Task<(UnitRegistry Registry, InmobiliariaDbContext Context)> RegistryAsAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (role, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var builder = new NpgsqlConnectionStringBuilder(superuser.Database.GetConnectionString()!)
        {
            Username = role,
            Password = password,
        };
        var context = new InmobiliariaDbContext(
            new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(builder.ConnectionString).Options);

        return (new UnitRegistry(context), context);
    }

    private static async Task<UnitListing> SavedAsync(Task<UnitResult> call)
    {
        var result = await call;
        Assert.True(result.Succeeded, $"Refused: {result.Refusal}.");
        return result.Unit!;
    }

    private static Task<long> CountUnitsAsync(DeskWorld world) => world.CountAsync("SELECT count(*) FROM units");

    /// <summary>The unit's row as text, to prove that ending a contract writes nothing on it and what a correction wrote.</summary>
    private static async Task<string> UnitRowAsync(DeskWorld world, Guid id)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT xmin::text || '|' || unit_type || '|' || address_street || '|' || address_number || '|' || " +
            "coalesce(address_floor, '') || '|' || coalesce(address_apartment, '') FROM units WHERE id = @id";
        command.Parameters.AddWithValue("id", id);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Seeds a contract covering <paramref name="unitId"/>, with a tenant unless none is named.</summary>
    private static async Task<Guid> SeedContractAsync(DeskWorld world, Guid unitId, string? tenantLastName = "Pérez", Action<Contract>? then = null)
    {
        await using var seed = world.NewContext();
        var contract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), 100_000m, [new UnitShare(unitId, 100m)]);
        if (tenantLastName is not null)
        {
            var tenant = new Party(Guid.NewGuid(), "Juan", tenantLastName);
            seed.Parties.Add(tenant);
            contract.AssignParty(tenant, PartyRole.Tenant);
        }

        then?.Invoke(contract);
        seed.Contracts.Add(contract);
        await seed.SaveChangesAsync();

        return contract.Id;
    }

    private static async Task ChangeContractAsync(DeskWorld world, Guid contractId, Action<Contract> change)
    {
        await using var context = world.NewContext();
        change(await context.Contracts.SingleAsync(c => c.Id == contractId));
        await context.SaveChangesAsync();
    }

    private static void GiveNotice(Contract contract) => contract.GiveNotice(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31));

    /// <summary>Spec Test 8: a unit no contract covers is available; an Active contract covers it, naming the tenant.</summary>
    [SkippableFact]
    public async Task Availability_FollowsAnActiveContract_AndNamesTheTenant()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var free = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, new UnitDraft("Belgrano", "40", "Sunchales", "Santa Fe", "2322"), false));
        var rented = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));
        await SeedContractAsync(world, rented.Id);

        var units = await Registry(world).ListAsync();

        Assert.True(units.Single(u => u.Id == free.Id).IsAvailable);
        var read = units.Single(u => u.Id == rented.Id);
        Assert.False(read.IsAvailable);
        Assert.Equal(new UnitCoverage(ContractStatus.Active, "Juan Pérez", null), read.Covering);
    }

    /// <summary>Spec Test 8: PendingTermination still covers it, with the move-out date.</summary>
    [SkippableFact]
    public async Task APendingTerminationContract_StillCovers_WithTheMoveOutDate()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var unit = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));
        await SeedContractAsync(world, unit.Id, then: GiveNotice);

        var read = Assert.Single(await Registry(world).ListAsync());

        Assert.False(read.IsAvailable);
        Assert.Equal(new UnitCoverage(ContractStatus.PendingTermination, "Juan Pérez", new DateOnly(2026, 10, 31)), read.Covering);
    }

    /// <summary>Spec Test 8 and Requirement 4's scenario: ending the contract frees the unit, and no unit row is written.</summary>
    [SkippableFact]
    public async Task EndingTheContract_MakesTheUnitAvailable_AndWritesNoUnitRow()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var unit = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));
        var contractId = await SeedContractAsync(world, unit.Id);
        Assert.False(Assert.Single(await Registry(world).ListAsync()).IsAvailable);
        var before = await UnitRowAsync(world, unit.Id);

        await ChangeContractAsync(world, contractId, c => c.End(EndReason.Expiry, new DateOnly(2026, 10, 31)));

        var read = Assert.Single(await Registry(world).ListAsync());
        Assert.True(read.IsAvailable);
        Assert.Null(read.Covering);
        Assert.Equal(before, await UnitRowAsync(world, unit.Id));
    }

    /// <summary>A contract with no tenant still covers its unit: availability must not depend on the join to the person.</summary>
    [SkippableFact]
    public async Task AContractWithoutATenant_StillCoversTheUnit()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var unit = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));
        await SeedContractAsync(world, unit.Id, tenantLastName: null);

        var read = Assert.Single(await Registry(world).ListAsync());

        Assert.False(read.IsAvailable);
        Assert.Null(read.Covering!.TenantName);
    }

    /// <summary>
    /// Spec Decision 30 lets a successor start while the previous contract waits for its move-out. The Active one is what
    /// the listing reports, since that is what a new contract would be refused for, and the unit appears once.
    /// </summary>
    [SkippableFact]
    public async Task ASuccessorBesideAPendingContract_ReportsTheActiveOne_Once()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var unit = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));
        await SeedContractAsync(world, unit.Id, tenantLastName: "Saliente", then: GiveNotice);
        await SeedContractAsync(world, unit.Id, tenantLastName: "Entrante");

        var read = Assert.Single(await Registry(world).ListAsync());

        Assert.Equal(ContractStatus.Active, read.Covering!.Status);
        Assert.Equal("Juan Entrante", read.Covering.TenantName);
        Assert.Null(read.Covering.MoveOutDate);
    }

    /// <summary>Design §2.2: availability is one query, whatever the number of units.</summary>
    [SkippableFact]
    public async Task TheList_IsOneQuery()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        for (var i = 1; i <= 3; i++)
        {
            var unit = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, new UnitDraft("Alem", $"{i}", "Sunchales", "Santa Fe", "2322"), false));
            await SeedContractAsync(world, unit.Id);
        }

        var counter = new CommandCounter();
        await using var context = new InmobiliariaDbContext(
            new DbContextOptionsBuilder<InmobiliariaDbContext>()
                .UseNpgsql(world.Database.Context.Database.GetConnectionString()!)
                .AddInterceptors(counter)
                .Options);

        var units = await new UnitRegistry(context).ListAsync();

        Assert.Equal(3, units.Count);
        Assert.Equal(1, counter.Commands);
    }

    /// <summary>Spec Requirement 4: by address, with accents folded.</summary>
    [SkippableFact]
    public async Task TheList_IsSortedByAddress_WithAccentsFolded()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        foreach (var street in new[] { "Zeballos", "Álvarez", "Belgrano" })
        {
            await SavedAsync(Registry(world).CreateAsync(UnitType.Property, new UnitDraft(street, "1", "Sunchales", "Santa Fe", "2322"), false));
        }

        var units = await Registry(world).ListAsync();

        Assert.Equal(["Álvarez", "Belgrano", "Zeballos"], units.Select(u => u.Address.Street).ToList());
    }

    /// <summary>Spec Requirements 4 and 5: both roles create both types, list them and correct an address.</summary>
    [SkippableTheory]
    [InlineData("inmobiliaria_admin")]
    [InlineData("inmobiliaria_empleado")]
    public async Task BothRoles_CreateListAndRelocate(string groupRole)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var seeded = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, new UnitDraft("Belgrano", "40", "Sunchales", "Santa Fe", "2322"), false));
        await SeedContractAsync(world, seeded.Id);
        var (registry, context) = await RegistryAsAsync(world, groupRole);
        await using (context)
        {
            var property = await SavedAsync(registry.CreateAsync(UnitType.Property, AlemDraft, false));
            var parking = await SavedAsync(registry.CreateAsync(UnitType.ParkingSpace, new UnitDraft("Alem", "1260", "Sunchales", "Santa Fe", "2322"), false));
            await SavedAsync(registry.RelocateAsync(property.Id, AlemDraft with { Number = "1259" }, false));

            var units = await registry.ListAsync();

            Assert.Equal(UnitType.ParkingSpace, units.Single(u => u.Id == parking.Id).Type);
            Assert.Equal("1259", units.Single(u => u.Id == property.Id).Address.Number);
            Assert.Equal("Juan Pérez", units.Single(u => u.Id == seeded.Id).Covering!.TenantName);
        }
    }

    /// <summary>Spec Requirement 5: what is stored is trimmed, with a blank floor absent.</summary>
    [SkippableFact]
    public async Task Creating_StoresTheCleanAddress()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var unit = await SavedAsync(Registry(world).CreateAsync(UnitType.ParkingSpace, new UnitDraft(" Alem ", "1258 ", "Sunchales", "Santa Fe", "2322", " ", " B "), false));

        Assert.EndsWith("|parking|Alem|1258||B", await UnitRowAsync(world, unit.Id));
    }

    /// <summary>Spec Requirement 5: an incomplete address is refused and stores nothing, both creating and correcting.</summary>
    [SkippableFact]
    public async Task AnIncompleteAddress_IsRefused_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var incomplete = AlemDraft with { Street = " " };
        var unit = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));
        var before = await UnitRowAsync(world, unit.Id);

        var created = await Registry(world).CreateAsync(UnitType.Property, incomplete, true);
        var relocated = await Registry(world).RelocateAsync(unit.Id, incomplete, true);

        Assert.Equal(UnitRefusal.AddressIncomplete, created.Refusal);
        Assert.Equal(UnitRefusal.AddressIncomplete, relocated.Refusal);
        Assert.Equal(1, await CountUnitsAsync(world));
        Assert.Equal(before, await UnitRowAsync(world, unit.Id));
    }

    /// <summary>Spec Test 9 and Requirement 5's scenario: an identical address warns naming the unit, and the operator can proceed.</summary>
    [SkippableTheory]
    [InlineData("Alem", "1258", "2", "B")]
    [InlineData("ALEM", "1258", "2", "b")]
    [InlineData(" Álem ", "1258", " 2", "B ")]
    public async Task AnIdenticalAddress_WarnsNamingTheUnit_AndCanProceed(string street, string number, string floor, string apartment)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var existing = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));
        var same = new UnitDraft(street, number, "Sunchales", "Santa Fe", "2322", floor, apartment);

        var warned = await Registry(world).CreateAsync(UnitType.Property, same, false);

        Assert.Equal(UnitRefusal.IdenticalAddressNotAccepted, warned.Refusal);
        Assert.Equal(existing.Id, warned.Existing!.Id);
        Assert.Equal(1, await CountUnitsAsync(world));

        await SavedAsync(Registry(world).CreateAsync(UnitType.Property, same, true));

        Assert.Equal(2, await CountUnitsAsync(world));
    }

    /// <summary>Another floor or apartment is another unit, in the same building: no warning.</summary>
    [SkippableTheory]
    [InlineData("3", "B")]
    [InlineData("2", "A")]
    [InlineData(null, null)]
    public async Task AnotherFloorOrApartment_DoesNotWarn(string? floor, string? apartment)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));

        await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft with { Floor = floor, Apartment = apartment }, false));

        Assert.Equal(2, await CountUnitsAsync(world));
    }

    /// <summary>Spec Test 9: correcting never changes the type, which nothing on the port can name, and keeps the row's identity.</summary>
    [SkippableFact]
    public async Task Relocating_ChangesTheAddress_AndNeverTheType()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var unit = await SavedAsync(Registry(world).CreateAsync(UnitType.ParkingSpace, AlemDraft, false));

        var moved = await SavedAsync(Registry(world).RelocateAsync(unit.Id, AlemDraft with { Street = "Roca", Floor = null }, false));

        Assert.Equal(UnitType.ParkingSpace, moved.Type);
        Assert.Equal(unit.Id, moved.Id);
        Assert.EndsWith("|parking|Roca|1258||B", await UnitRowAsync(world, unit.Id));
        Assert.Equal(1, await CountUnitsAsync(world));
    }

    /// <summary>A correction to another unit's address warns; its own address does not count, so changing a letter's case is free.</summary>
    [SkippableFact]
    public async Task Relocating_WarnsOnlyAboutAnotherUnit()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var alem = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));
        var roca = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft with { Street = "Roca" }, false));

        await SavedAsync(Registry(world).RelocateAsync(alem.Id, AlemDraft with { Apartment = "b" }, false));
        var warned = await Registry(world).RelocateAsync(roca.Id, AlemDraft, false);

        Assert.Equal(UnitRefusal.IdenticalAddressNotAccepted, warned.Refusal);
        Assert.Equal(alem.Id, warned.Existing!.Id);
        Assert.Contains("|Roca|", await UnitRowAsync(world, roca.Id));

        await SavedAsync(Registry(world).RelocateAsync(roca.Id, AlemDraft, true));

        Assert.Contains("|Alem|", await UnitRowAsync(world, roca.Id));
    }

    /// <summary>A correction keeps reporting the contract that covers the unit, which a correction does not touch.</summary>
    [SkippableFact]
    public async Task Relocating_ACoveredUnit_ReportsItsCoverage()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var unit = await SavedAsync(Registry(world).CreateAsync(UnitType.Property, AlemDraft, false));
        await SeedContractAsync(world, unit.Id);

        var moved = await SavedAsync(Registry(world).RelocateAsync(unit.Id, AlemDraft with { Number = "1260" }, false));

        Assert.Equal("Juan Pérez", moved.Covering!.TenantName);
    }

    [SkippableFact]
    public async Task RelocatingAUnitThatIsGone_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var result = await Registry(world).RelocateAsync(Guid.NewGuid(), AlemDraft, false);

        Assert.Equal(UnitRefusal.UnitNotFound, result.Refusal);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Commands { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
