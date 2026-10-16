using Inmobiliaria.Domain.Access;
using Inmobiliaria.Infrastructure.Persistence;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Task 6.9: the starting-number adapter surfaces the database's own answer. Test 65 already proves the
/// grant at the SQL level; this proves the adapter neither hides a refusal nor writes anything but the
/// starting number.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReciboNumberingTests
{
    private readonly PostgresFixture _fixture;

    public ReciboNumberingTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task AnAdminLogin_SetsTheStartingNumber_AndTheCounterFollowsIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);
        var username = $"a{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(world.Database.Context, username, UserRole.Admin);

        await using var asAdmin = AccessTestSupport.BuildDbContextAs(world.Database.Context, username);
        await new ReciboNumbering(asAdmin).SetStartingNumberAsync(31100);

        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        Assert.Equal((31100, 31100), await ReciboTestSupport.ReadCounterAsync(connection));
    }

    [SkippableFact]
    public async Task AnEmpleadoLogin_IsRefused_AndTheCounterIsUntouched()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);
        var username = $"e{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(world.Database.Context, username, UserRole.Empleado);

        await using var asEmpleado = AccessTestSupport.BuildDbContextAs(world.Database.Context, username);
        var failure = await Record.ExceptionAsync(() => new ReciboNumbering(asEmpleado).SetStartingNumberAsync(31100));

        var refused = Assert.IsType<PostgresException>(failure is PostgresException ? failure : failure?.InnerException);
        Assert.Equal("42501", refused.SqlState);

        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        Assert.Equal(((int?)null, (int?)null), await ReciboTestSupport.ReadCounterAsync(connection));
    }

    [SkippableFact]
    public async Task AMissingCounterRow_IsNotASilentSuccess()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture, startingNumber: null);

        // The singleton row can only vanish with the triggers off (a botched restore); nothing in the schema allows it.
        await using (var connection = await ReciboTestSupport.OpenAsync(world.Database.Context))
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await ReciboTestSupport.ExecAsync(connection, transaction, "SET LOCAL session_replication_role = replica");
            Assert.Equal(1, await ReciboTestSupport.ExecAsync(connection, transaction, "DELETE FROM recibo_numbering"));
            await transaction.CommitAsync();
        }

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ReciboNumbering(world.NewContext()).SetStartingNumberAsync(31100));

        Assert.Contains("recibo_numbering", failure.Message);
    }
}
