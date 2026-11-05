using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Tests 1 to 7 (Requirements 1 to 3) against real Postgres, as real Admin and Empleado
/// logins, with effects read back by raw SQL. Each test has its own database, because a search answers over every
/// row. All people and CUILs are fictional; the CUILs are repeated digits with a computed check digit.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PartyRegistryTests
{
    private const string JuanCuil = "20111111112";

    private readonly PostgresFixture _fixture;

    public PartyRegistryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<DeskWorld> WorldAsync() => DeskWorld.CreateAsync(_fixture);

    private static PartyRegistry Registry(DeskWorld world) => new(world.NewContext());

    /// <summary>A registry on a context logged in as a real member of <paramref name="groupRole"/>.</summary>
    private static async Task<(PartyRegistry Registry, InmobiliariaDbContext Context)> RegistryAsAsync(DeskWorld world, string groupRole)
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

        return (new PartyRegistry(context), context);
    }

    private static async Task<PartyListing> SavedAsync(Task<PartyResult> call)
    {
        var result = await call;
        Assert.True(result.Succeeded, $"Refused: {result.Refusal}.");
        return result.Party!;
    }

    private static Task<long> CountPartiesAsync(DeskWorld world) => world.CountAsync("SELECT count(*) FROM parties");

    private static async Task<(string? Dni, string? Cuil)> StoredIdentifiersAsync(DeskWorld world, Guid id)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT dni, cuil FROM parties WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    /// <summary>Spec Test 1: ignores case and accents, matches part of a name, and finds no one else.</summary>
    [SkippableTheory]
    [InlineData("nunez")]
    [InlineData("NÚÑEZ")]
    [InlineData("uñe")]
    [InlineData("maria")]
    [InlineData("maria nunez")]
    [InlineData("nunez, maria")]
    public async Task SearchByName_IgnoresCaseAndAccents_AndMatchesPart(string text)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var maria = await SavedAsync(Registry(world).CreateAsync(new PartyDraft("María", "Núñez", "30123456"), false));
        await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Juan", "Pérez", "28111222"), false));

        var found = await Registry(world).SearchAsync(text);

        Assert.Equal(maria.Id, Assert.Single(found).Id);
    }

    /// <summary>Spec Test 2: a formatted DNI or CUIL, or part of it, finds the person.</summary>
    [SkippableTheory]
    [InlineData("30.123.456")]
    [InlineData("30123456")]
    [InlineData("30-123-456")]
    [InlineData("20-11111111-2")]
    [InlineData("20111111112")]
    public async Task SearchByFormattedIdentifier_FindsThePerson(string text)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var juan = await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Juan", "Pérez", "30123456", JuanCuil), false));
        await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Ana", "Gómez", "28111222"), false));

        var found = await Registry(world).SearchAsync(text);

        Assert.Equal(juan.Id, Assert.Single(found).Id);
    }

    /// <summary>Spec Requirement 1: blank lists everyone, sorted by surname with accents folded, past the search's result cap.</summary>
    [SkippableFact]
    public async Task BlankSearch_ListsEveryone_BySurname()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        await using (var seed = world.NewContext())
        {
            seed.Parties.AddRange(Enumerable.Range(0, AccountSearch.MaxResults + 1).Select(i => new Party(Guid.NewGuid(), "Uno", $"Zeta{i:00}")));
            await seed.SaveChangesAsync();
        }

        await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Ana", "Álvarez", "28111222"), false));
        await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Juan", "Bravo", "30123456"), false));

        var everyone = await Registry(world).SearchAsync("  ");

        Assert.Equal(AccountSearch.MaxResults + 3, everyone.Count);
        Assert.Equal(["Álvarez", "Bravo", "Zeta00"], everyone.Take(3).Select(p => p.LastName).ToList());
    }

    /// <summary>Spec Requirement 1: both roles can list and search.</summary>
    [SkippableTheory]
    [InlineData("inmobiliaria_admin")]
    [InlineData("inmobiliaria_empleado")]
    public async Task BothRoles_CreateSearchAndCorrect(string groupRole)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var (registry, context) = await RegistryAsAsync(world, groupRole);
        await using (context)
        {
            var created = await SavedAsync(registry.CreateAsync(new PartyDraft("María", "Núñez", "30.123.456"), false));
            var corrected = await SavedAsync(registry.CorrectAsync(
                created.Id, new PartyDraft("María Inés", "Núñez", "30.123.456", JuanCuil, "m@example.com"), false));

            var found = await registry.SearchAsync("nunez");

            Assert.Equal(created.Id, Assert.Single(found).Id);
            Assert.Equal("María Inés", found[0].FirstName);
            Assert.Equal("m@example.com", corrected.Email);
        }

        Assert.Equal(("30123456", JuanCuil), await StoredIdentifiersAsync(world, Assert.Single(await Registry(world).SearchAsync("nunez")).Id));
    }

    /// <summary>Spec Test 3: stored as digits only, so the two spellings collide.</summary>
    [SkippableFact]
    public async Task Identifiers_AreStoredAsDigitsOnly_AndTwoSpellingsCollide()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var juan = await SavedAsync(Registry(world).CreateAsync(new PartyDraft(" Juan ", " Pérez ", "30.123.456", "20-11111111-2"), false));
        var collision = await Registry(world).CreateAsync(new PartyDraft("Otro", "Señor", "30123456"), false);

        Assert.Equal(("30123456", JuanCuil), await StoredIdentifiersAsync(world, juan.Id));
        Assert.Equal(("Juan", "Pérez"), (juan.FirstName, juan.LastName));
        Assert.Equal(PartyRefusal.DniTaken, collision.Refusal);
        Assert.Equal(1, await CountPartiesAsync(world));
    }

    /// <summary>Spec Test 4: refused from the unique index, naming the holder; nothing written.</summary>
    [SkippableTheory]
    [InlineData("30.123.456", "27333333339", PartyRefusal.DniTaken)]
    [InlineData("28111222", "20-11111111-2", PartyRefusal.CuilTaken)]
    public async Task ADuplicateIdentifier_IsRefusedNamingTheHolder_AndWritesNothing(string dni, string cuil, PartyRefusal expected)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var juan = await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Juan", "Pérez", "30123456", JuanCuil), false));

        var result = await Registry(world).CreateAsync(new PartyDraft("Otro", "Señor", dni, cuil), false);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Refusal);
        Assert.Equal(juan.Id, result.Holder!.Id);
        Assert.Equal("Juan Pérez", result.Holder.FullName);
        Assert.Contains("Juan Pérez", result.Refusal!.Value.OperatorText(result.Holder.FullName));
        Assert.Null(result.Party);
        Assert.Equal(1, await CountPartiesAsync(world));
    }

    /// <summary>Spec Test 5: refusals by value, each its own, writing nothing, including a CUIL with a wrong check digit.</summary>
    [SkippableTheory]
    [InlineData("Juan", "Pérez", "12345", null, PartyRefusal.DniInvalid)]
    [InlineData("Juan", "Pérez", "123456789", null, PartyRefusal.DniInvalid)]
    [InlineData("Juan", "Pérez", null, "20-11111111-3", PartyRefusal.CuilInvalid)]
    [InlineData(" ", "Pérez", "30123456", null, PartyRefusal.NameMissing)]
    public async Task AnInvalidDraft_IsRefused_AndWritesNothing(string first, string last, string? dni, string? cuil, PartyRefusal expected)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var result = await Registry(world).CreateAsync(new PartyDraft(first, last, dni, cuil), true);

        Assert.Equal(expected, result.Refusal);
        Assert.Equal(0, await CountPartiesAsync(world));
    }

    /// <summary>Spec Test 6: neither identifier is refused until accepted, then stored as two nulls; two such persons coexist.</summary>
    [SkippableFact]
    public async Task NeitherIdentifier_IsAcceptedOnlyAfterTheOperatorAccepts()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();

        var unaccepted = await Registry(world).CreateAsync(new PartyDraft("Hans", "Müller", " ", "-"), false);
        var countBefore = await CountPartiesAsync(world);
        var hans = await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Hans", "Müller", " ", "-"), true));
        var other = await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Eva", "Müller"), true));

        Assert.Equal(PartyRefusal.NoIdentifierNotAccepted, unaccepted.Refusal);
        Assert.Equal(0, countBefore);
        Assert.True(hans.HasNoIdentifier);
        Assert.Equal(((string?)null, (string?)null), await StoredIdentifiersAsync(world, hans.Id));
        Assert.Equal(((string?)null, (string?)null), await StoredIdentifiersAsync(world, other.Id));
        Assert.Equal(2, await CountPartiesAsync(world));
    }

    /// <summary>Spec Requirement 3: a correction writes every field; its own identifiers do not collide with themselves.</summary>
    [SkippableFact]
    public async Task Correcting_WritesEveryField_AndKeepingOwnIdentifiersIsNoCollision()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var juan = await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Juan", "Perez", "30123456", JuanCuil), false));

        var corrected = await SavedAsync(Registry(world).CorrectAsync(
            juan.Id, new PartyDraft("Juan Carlos", "Pérez", "30.123.456", "20-11111111-2", "j@example.com", "3493 000000", "Bv. Belgrano 123"), false));

        var read = Assert.Single(await Registry(world).SearchAsync("perez"));
        Assert.Equal(corrected, read);
        Assert.Equal(
            new PartyListing(juan.Id, "Juan Carlos", "Pérez", "30123456", JuanCuil, "j@example.com", "3493 000000", "Bv. Belgrano 123"),
            read);
    }

    /// <summary>Spec Requirement 3 with Requirement 2's checks: a correction is refused like a creation and changes nothing.</summary>
    [SkippableFact]
    public async Task ACorrection_IsCheckedLikeACreation_AndARefusalChangesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var juan = await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Juan", "Pérez", "30123456", JuanCuil), false));
        var ana = await SavedAsync(Registry(world).CreateAsync(new PartyDraft("Ana", "Gómez", "28111222"), false));

        var taken = await Registry(world).CorrectAsync(ana.Id, new PartyDraft("Anita", "Gómez", "30.123.456"), false);
        var invalid = await Registry(world).CorrectAsync(ana.Id, new PartyDraft("Anita", "Gómez", "28111222", "20111111113"), false);
        var noIdentifier = await Registry(world).CorrectAsync(ana.Id, new PartyDraft("Anita", "Gómez"), false);
        var missing = await Registry(world).CorrectAsync(Guid.NewGuid(), new PartyDraft("Anita", "Gómez", "27111222"), false);

        Assert.Equal(PartyRefusal.DniTaken, taken.Refusal);
        Assert.Equal(juan.Id, taken.Holder!.Id);
        Assert.Equal(PartyRefusal.CuilInvalid, invalid.Refusal);
        Assert.Equal(PartyRefusal.NoIdentifierNotAccepted, noIdentifier.Refusal);
        Assert.Equal(PartyRefusal.PartyNotFound, missing.Refusal);
        var read = Assert.Single(await Registry(world).SearchAsync("gomez"));
        Assert.Equal(("Ana", "28111222"), (read.FirstName, read.Dni));
    }

    /// <summary>Spec Test 7: the contract shows the new name; the recibo already issued keeps what it printed.</summary>
    [SkippableFact]
    public async Task CorrectingAName_LeavesAnIssuedReciboUnchanged()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await WorldAsync();
        var accountId = await world.SeedAccountAsync();
        await world.MaterialiseAsync(accountId, DeskWorld.Today);
        var payment = await DeskWorld.PayAsync(world.NewDesk(), accountId);
        Assert.Null(payment.Refusal);
        var reciboId = await world.LatestReciboIdAsync(accountId);

        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        var tenantId = await ReciboTestSupport.ScalarAsync<Guid>(
            connection,
            """
            SELECT cp.party_id FROM contract_parties cp
            JOIN contract_accounts a ON a.contract_id = cp.contract_id
            WHERE a.id = @account AND cp.role = 'Tenant'
            """,
            ("account", accountId));
        var printed = await ReciboTestSupport.ScalarAsync<string>(
            connection, "SELECT tenant_name FROM recibos WHERE id = @id", ("id", reciboId));

        Assert.Contains("Perez", printed);

        await SavedAsync(Registry(world).CorrectAsync(tenantId, new PartyDraft("María Inés", "Pérez", "30123456"), false));

        await using var reader = world.NewContext();
        var contract = await reader.Contracts.AsNoTracking().Include(c => c.Parties)
            .SingleAsync(c => c.Parties.Any(p => p.PartyId == tenantId));
        var shownPartyId = contract.Parties.Single(cp => cp.Role == PartyRole.Tenant).PartyId;
        var shownOnContract = await reader.Parties.AsNoTracking().SingleAsync(p => p.Id == shownPartyId);

        Assert.Equal("María Inés Pérez", $"{shownOnContract.FirstName} {shownOnContract.LastName}");
        Assert.Equal(printed, await ReciboTestSupport.ScalarAsync<string>(
            connection, "SELECT tenant_name FROM recibos WHERE id = @id", ("id", reciboId)));
    }

    /// <summary>An unknown failure is not turned into a refusal: it surfaces as the bug it is, as on the desk.</summary>
    [Theory]
    [InlineData("23505", "ix_parties_dni", PartyRefusal.DniTaken)]
    [InlineData("23505", "ix_parties_cuil", PartyRefusal.CuilTaken)]
    [InlineData("23505", "pk_parties", null)]
    [InlineData("42501", "ix_parties_dni", null)]
    [InlineData("23505", null, null)]
    public void TheErrorMap_ReadsSqlStateAndConstraintName(string sqlState, string? constraint, PartyRefusal? expected)
    {
        Assert.Equal(expected, PartyRegistryErrorMap.For(sqlState, constraint));
    }
}
