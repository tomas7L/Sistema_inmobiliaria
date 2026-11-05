using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Test 14 and design D13 against real Postgres, as real login roles: the database refuses a
/// second tenant whoever asks, and a removed party is ignored by the index and hidden from every ordinary read.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContractPartyGuardTests
{
    private const string UniqueViolation = "23505";

    private readonly PostgresFixture _fixture;

    public ContractPartyGuardTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(Guid ContractId, Guid TenantId)> SeedAsync(InmobiliariaDbContext context)
    {
        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Fake Street", Guid.NewGuid().ToString("N")[..6], "Springfield", "Buenos Aires", "1000"));
        var tenant = new Party(Guid.NewGuid(), "Primero", "Inquilino");
        var contract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), 100_000m, [new UnitShare(unit.Id, 100m)]);
        contract.AssignParty(tenant, PartyRole.Tenant);
        context.Units.Add(unit);
        context.Parties.Add(tenant);
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        return (contract.Id, tenant.Id);
    }

    private static async Task<Guid> InsertPartyAsync(NpgsqlConnection connection)
    {
        var id = Guid.NewGuid();
        await ReciboTestSupport.ExecAsync(
            connection, "INSERT INTO parties (id, first_name, last_name) VALUES (@id, 'Otra', 'Persona')", ("id", id));
        return id;
    }

    private static Task<int> InsertRoleAsync(NpgsqlConnection connection, Guid contractId, Guid partyId, string role) =>
        ReciboTestSupport.ExecAsync(
            connection,
            "INSERT INTO contract_parties (contract_id, party_id, role) VALUES (@contract, @party, @role)",
            ("contract", contractId), ("party", partyId), ("role", role));

    private static async Task<NpgsqlConnection> OpenAsAsync(InmobiliariaDbContext superuser, string groupRole)
    {
        var (roleName, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var connection = BuildConnectionAs(superuser, roleName, password);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Spec Test 14: a second tenant is refused by raw SQL, as each role.</summary>
    [SkippableTheory]
    [InlineData("inmobiliaria_admin")]
    [InlineData("inmobiliaria_empleado")]
    public async Task ASecondTenant_IsRefusedByTheDatabase(string groupRole)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var superuser = _fixture.CreateDbContext();
        var (contractId, _) = await SeedAsync(superuser);
        await using var session = await OpenAsAsync(superuser, groupRole);
        var other = await InsertPartyAsync(session);

        var refused = await ReciboTestSupport.RefusedAsync(() => InsertRoleAsync(session, contractId, other, "Tenant"));

        Assert.Equal(UniqueViolation, refused.SqlState);
        Assert.Equal("ux_contract_parties_one_tenant", refused.ConstraintName);
    }

    /// <summary>The index is about tenants only: lessors and codeudores stay as many as a contract has.</summary>
    [SkippableFact]
    public async Task SeveralLessorsAndCodebtors_AreStillAccepted()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var superuser = _fixture.CreateDbContext();
        var (contractId, _) = await SeedAsync(superuser);
        await using var session = await OpenAsAsync(superuser, "inmobiliaria_empleado");

        foreach (var role in new[] { "Lessor", "Lessor", "Codebtor", "Codebtor" })
        {
            await InsertRoleAsync(session, contractId, await InsertPartyAsync(session), role);
        }

        Assert.Equal(5L, await ReciboTestSupport.ScalarAsync<long>(
            session, "SELECT count(*) FROM contract_parties WHERE contract_id = @id", ("id", contractId)));
    }

    /// <summary>Design D13: a removed tenant no longer counts for the index, and every ordinary read stops seeing the row.</summary>
    [SkippableFact]
    public async Task ARemovedTenant_FreesTheIndex_AndIsHiddenFromReads()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var superuser = _fixture.CreateDbContext();
        var (contractId, tenantId) = await SeedAsync(superuser);
        await using var connection = await ReciboTestSupport.OpenAsync(superuser);
        await ReciboTestSupport.ExecAsync(
            connection,
            "UPDATE contract_parties SET removed_at = now() WHERE contract_id = @contract AND party_id = @party",
            ("contract", contractId), ("party", tenantId));

        var replacement = await InsertPartyAsync(connection);
        await InsertRoleAsync(connection, contractId, replacement, "Tenant");

        await using var fresh = _fixture.CreateDbContext();
        var read = await fresh.Contracts.AsNoTracking().Include(c => c.Parties).SingleAsync(c => c.Id == contractId);
        Assert.Equal(replacement, Assert.Single(read.Parties).PartyId);
        Assert.Equal(2, await fresh.ContractParties.IgnoreQueryFilters().CountAsync(p => p.ContractId == contractId));
    }
}
