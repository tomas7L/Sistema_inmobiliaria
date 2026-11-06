using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Test 30 (Requirement 11): M4 leaves exactly one IPC, RIPTE and ICL, whatever the database
/// already held, as the test database holds "ICL". Each test migrates its own database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IndexSeedMigrationTests
{
    private const string BeforeTheSeed = "20261106193800_AddLoadedByMistakeEndReason";

    private readonly PostgresFixture _fixture;

    public IndexSeedMigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<long> ActiveNamedAsync(IsolatedDatabase database, string name)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);

        return await ReciboTestSupport.ScalarAsync<long>(
            connection, "SELECT count(*) FROM economic_indices WHERE name = @n AND discontinued_from IS NULL", ("n", name));
    }

    /// <summary>An isolated database arrives fully migrated, so this takes it back to before the seed, with an empty catalog.</summary>
    private static async Task<IMigrator> BeforeTheSeedWithAnEmptyCatalogAsync(IsolatedDatabase database)
    {
        var migrator = database.Context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheSeed);
        await using var connection = await ReciboTestSupport.OpenAsync(database.Context);
        await ReciboTestSupport.ExecAsync(connection, "DELETE FROM economic_indices");

        return migrator;
    }

    [SkippableFact]
    public async Task AnEmptyDatabase_EndsWithExactlyOneOfEach()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await IsolatedDatabase.CreateAsync(_fixture);

        var migrator = await BeforeTheSeedWithAnEmptyCatalogAsync(database);
        Assert.Equal(0, await ActiveNamedAsync(database, "IPC"));

        await migrator.MigrateAsync();

        Assert.Equal(1, await ActiveNamedAsync(database, "IPC"));
        Assert.Equal(1, await ActiveNamedAsync(database, "RIPTE"));
        Assert.Equal(1, await ActiveNamedAsync(database, "ICL"));
    }

    [SkippableFact]
    public async Task ADatabaseAlreadyHoldingIcl_KeepsThatRowAndItsValues_AndGainsTheOthers()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var migrator = await BeforeTheSeedWithAnEmptyCatalogAsync(database);
        var icl = Guid.NewGuid();
        await using (var connection = await ReciboTestSupport.OpenAsync(database.Context))
        {
            await ReciboTestSupport.ExecAsync(connection, "INSERT INTO economic_indices (id, name) VALUES (@id, 'ICL')", ("id", icl));
            await ReciboTestSupport.ExecAsync(
                connection,
                "INSERT INTO index_values (id, economic_index_id, period, level) VALUES (gen_random_uuid(), @id, '2026-06-01', 25.08)",
                ("id", icl));
        }

        await migrator.MigrateAsync();

        Assert.Equal(1, await ActiveNamedAsync(database, "ICL"));
        Assert.Equal(1, await ActiveNamedAsync(database, "IPC"));
        Assert.Equal(1, await ActiveNamedAsync(database, "RIPTE"));
        await using var reader = await ReciboTestSupport.OpenAsync(database.Context);
        Assert.Equal(icl, await ReciboTestSupport.ScalarAsync<Guid>(reader, "SELECT id FROM economic_indices WHERE name = 'ICL'"));
        Assert.Equal(25.08m, await ReciboTestSupport.ScalarAsync<decimal>(reader, "SELECT level FROM index_values WHERE economic_index_id = @id", ("id", icl)));
    }

    [SkippableFact]
    public async Task GoingBackDown_RemovesNoCatalogRow_SinceValuesMayReferenceThem()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var database = await IsolatedDatabase.CreateAsync(_fixture);
        var migrator = database.Context.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeTheSeed);

        Assert.Equal(1, await ActiveNamedAsync(database, "IPC"));
        Assert.Equal(1, await ActiveNamedAsync(database, "RIPTE"));
        Assert.Equal(1, await ActiveNamedAsync(database, "ICL"));
    }
}
