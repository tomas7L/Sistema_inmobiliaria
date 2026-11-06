using Npgsql;
using static Inmobiliaria.Infrastructure.Tests.RoleTestHelpers;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Tests 94 and 95 (Requirement 12, Decision 42) by raw SQL: the database itself records a change
/// to a level, whatever wrote it, as real Admin and Empleado logins, since a superuser proves nothing about a grant.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IndexValueCorrectionSchemaTests
{
    private const string Admin = "inmobiliaria_admin";
    private const string Empleado = "inmobiliaria_empleado";

    private readonly PostgresFixture _fixture;

    public IndexValueCorrectionSchemaTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Guid> SeedValueAsync(DeskWorld world, decimal level = 9_440m)
    {
        var id = Guid.NewGuid();
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await ReciboTestSupport.ExecAsync(
            connection,
            "INSERT INTO index_values (id, economic_index_id, period, level) " +
            "SELECT @id, id, '2026-08-01', @level FROM economic_indices WHERE name = 'IPC'",
            ("id", id),
            ("level", level));

        return id;
    }

    private static async Task<(NpgsqlConnection Connection, string Login)> LoginAsync(DeskWorld world, string groupRole)
    {
        var superuser = world.Database.Context;
        var (login, password) = await CreateTestLoginRoleAsync(superuser, groupRole);
        var connection = BuildConnectionAs(superuser, login, password);
        await connection.OpenAsync();

        return (connection, login);
    }

    private static Task<long> RecordsAsync(DeskWorld world, Guid value) =>
        world.CountAsync("SELECT count(*) FROM index_value_corrections WHERE index_value_id = @v", ("v", value));

    private static async Task<T> ReadAsync<T>(DeskWorld world, string column, Guid value)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);

        return await ReciboTestSupport.ScalarAsync<T>(
            connection, $"SELECT {column} FROM index_value_corrections WHERE index_value_id = @v", ("v", value));
    }

    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task ChangingALevel_RecordsWhoWhenAndBothLevels(string groupRole)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var value = await SeedValueAsync(world);
        var (connection, login) = await LoginAsync(world, groupRole);
        await using var _ = connection;
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        await ReciboTestSupport.ExecAsync(connection, "UPDATE index_values SET level = 9400 WHERE id = @v", ("v", value));

        Assert.Equal(1, await RecordsAsync(world, value));
        Assert.Equal(9_440m, await ReadAsync<decimal>(world, "previous_level", value));
        Assert.Equal(9_400m, await ReadAsync<decimal>(world, "new_level", value));
        Assert.Equal(login, await ReadAsync<string>(world, "recorded_by::text", value));
        Assert.InRange(await ReadAsync<DateTime>(world, "recorded_at", value), before.UtcDateTime, DateTime.UtcNow.AddMinutes(1));
    }

    [SkippableFact]
    public async Task SavingTheSameLevel_RecordsNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var value = await SeedValueAsync(world);
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);

        await ReciboTestSupport.ExecAsync(connection, "UPDATE index_values SET level = 9440.000000 WHERE id = @v", ("v", value));

        Assert.Equal(0, await RecordsAsync(world, value));
    }

    [SkippableFact]
    public async Task EachOfTwoCorrections_RecordsTheLevelItReallyOverwrote()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var value = await SeedValueAsync(world);
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);

        await ReciboTestSupport.ExecAsync(connection, "UPDATE index_values SET level = 9400 WHERE id = @v", ("v", value));
        await ReciboTestSupport.ExecAsync(connection, "UPDATE index_values SET level = 9410 WHERE id = @v", ("v", value));

        Assert.Equal(2, await RecordsAsync(world, value));
        Assert.Equal(1, await world.CountAsync(
            "SELECT count(*) FROM index_value_corrections WHERE index_value_id = @v AND previous_level = 9400 AND new_level = 9410", ("v", value)));
    }

    [SkippableTheory]
    [InlineData(Admin)]
    [InlineData(Empleado)]
    public async Task NeitherRole_CanUpdateOrDeleteARecord(string groupRole)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var value = await SeedValueAsync(world);
        var (connection, _) = await LoginAsync(world, groupRole);
        await using var __ = connection;
        await ReciboTestSupport.ExecAsync(connection, "UPDATE index_values SET level = 9400 WHERE id = @v", ("v", value));

        await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(
            connection, "UPDATE index_value_corrections SET previous_level = 1 WHERE index_value_id = @v", ("v", value)));
        await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(
            connection, "DELETE FROM index_value_corrections WHERE index_value_id = @v", ("v", value)));

        Assert.Equal(1, await RecordsAsync(world, value));
        Assert.Equal(9_440m, await ReadAsync<decimal>(world, "previous_level", value));
    }

    /// <summary>A grant is one wall and the trigger another: this one holds for the owner too, who has every grant.</summary>
    [SkippableFact]
    public async Task EvenTheOwner_CannotUpdateOrDeleteARecord()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var value = await SeedValueAsync(world);
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await ReciboTestSupport.ExecAsync(connection, "UPDATE index_values SET level = 9400 WHERE id = @v", ("v", value));

        var update = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(
            connection, "UPDATE index_value_corrections SET previous_level = 1 WHERE index_value_id = @v", ("v", value)));
        var delete = await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(
            connection, "DELETE FROM index_value_corrections WHERE index_value_id = @v", ("v", value)));

        Assert.Equal("index_value_corrections_append_only", update.ConstraintName);
        Assert.Equal("index_value_corrections_append_only", delete.ConstraintName);
    }

    [SkippableFact]
    public async Task AnInsert_CannotChooseItsAuthorOrItsMoment()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var value = await SeedValueAsync(world);
        var (connection, login) = await LoginAsync(world, Empleado);
        await using var _ = connection;

        await ReciboTestSupport.ExecAsync(
            connection,
            "INSERT INTO index_value_corrections (id, index_value_id, previous_level, new_level, recorded_by, recorded_at) " +
            "VALUES (gen_random_uuid(), @v, 1, 2, 'otra_persona', '2000-01-01')",
            ("v", value));

        Assert.Equal(login, await ReadAsync<string>(world, "recorded_by::text", value));
        Assert.True(await ReadAsync<DateTime>(world, "recorded_at", value) > DateTime.UtcNow.AddMinutes(-5));
    }

    [SkippableFact]
    public async Task ARecord_KeepsItsValueFromBeingDeleted()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var value = await SeedValueAsync(world);
        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        await ReciboTestSupport.ExecAsync(connection, "UPDATE index_values SET level = 9400 WHERE id = @v", ("v", value));

        await ReciboTestSupport.RefusedAsync(() => ReciboTestSupport.ExecAsync(connection, "DELETE FROM index_values WHERE id = @v", ("v", value)));

        Assert.Equal(1, await RecordsAsync(world, value));
    }
}
