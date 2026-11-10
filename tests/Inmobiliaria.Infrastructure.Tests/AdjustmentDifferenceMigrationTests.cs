using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirement 20, Test 67: a recibo issued before M7 <c>AddAdjustmentDifference</c> reads identically
/// after it, and M7 rolls back and applies again over existing recibos.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdjustmentDifferenceMigrationTests
{
    private const string BeforeM7 = "20261110135438_AddAdjustmentAppliesFrom";

    private readonly PostgresFixture _fixture;

    public AdjustmentDifferenceMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    private static async Task<List<string>> LinesAsync(DeskWorld world)
    {
        await using var read = world.NewContext();
        return await read.Database
            .SqlQuery<string>($"SELECT r.number::text || '|' || l.position::text || '|' || l.kind || '|' || l.amount::text AS \"Value\" FROM recibo_lines l JOIN recibos r ON r.id = l.recibo_id ORDER BY r.number, l.position")
            .ToListAsync();
    }

    [SkippableFact]
    public async Task Tests65And66_ThroughTheDesk_ThePeriodIsPaidWithItsCharge_OnBothRecibos()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var account = await world.SeedAccountAsync();
        await world.MaterialiseAsync(account, DeskWorld.Today);
        var oldest = DeskWorld.OldestPeriod;
        await using (var write = world.NewContext())
        {
            var stored = await write.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == account);
            var accrual = stored.Movements.Single(m => m.Kind == Domain.Accounts.MovementKind.RentAccrual && m.Period == oldest);
            stored.Append(Domain.Accounts.AccountMovement.AdjustmentDifference(Guid.NewGuid(), 60_000m, oldest, accrual.OccurredOn));
            await write.SaveChangesAsync();
        }

        var desk = world.NewDesk();
        var probe = await desk.PreviewAsync(new Domain.Collection.CollectionRequest(account, oldest, null, 0m));
        var rentOnly = await desk.PreviewAsync(new Domain.Collection.CollectionRequest(account, oldest, null, probe.LedgerFigure - 60_000m));
        Assert.Equal(Domain.Collection.CollectionRefusal.AmountNotTotal, rentOnly.Refusal);

        var paid = await DeskWorld.PayAsync(desk, account, oldest);

        Assert.True(paid.Succeeded, paid.Refusal?.ToString());
        Assert.Equal(2L, await world.CountAsync(
            "SELECT count(*) FROM recibo_lines WHERE kind = 'AdjustmentDifference' AND amount = 60000"));
        Assert.Equal(0L, await world.CountAsync(
            "SELECT count(*) FROM (SELECT 1 FROM account_movements WHERE account_id = @a AND period = @p GROUP BY period HAVING sum(amount) <> 0) owed",
            ("a", account), ("p", oldest)));
    }

    /// <summary>The S10 review's R4-001: rolling M7 back over a charged difference is refused before anything changes.</summary>
    [SkippableFact]
    public async Task RollingM7Back_OverAChargedDifference_IsRefused_AndChangesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var account = await world.SeedAccountAsync();
        await using (var write = world.NewContext())
        {
            var stored = await write.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == account);
            stored.Append(Domain.Accounts.AccountMovement.AdjustmentDifference(Guid.NewGuid(), 60_000m, DeskWorld.OldestPeriod, DeskWorld.OldestPeriod.AddDays(9)));
            await write.SaveChangesAsync();
        }

        var migrator = world.Database.Context.GetService<IMigrator>();
        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => migrator.MigrateAsync(BeforeM7));

        Assert.Contains("cannot be rolled back", refused.MessageText);
        Assert.Equal(1L, await world.CountAsync(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'rent_adjustments' AND column_name = 'difference_movement_id'"));
        Assert.Equal(1L, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'AdjustmentDifference'"));
    }

    /// <summary>The S10 review's R3-001: the guard's second branch, a recibo line of the kind with no movement of it.</summary>
    [SkippableFact]
    public async Task RollingM7Back_OverAPrintedDifferenceAlone_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var account = await world.SeedAccountAsync();
        await world.MaterialiseAsync(account, DeskWorld.Today);
        Assert.True((await DeskWorld.PayAsync(world.NewDesk(), account)).Succeeded);
        await using (var write = world.NewContext())
        {
            await write.Database.ExecuteSqlAsync(
                $"INSERT INTO recibo_lines (id, recibo_id, kind, position, amount) SELECT {Guid.NewGuid()}, id, 'AdjustmentDifference', 99, 1 FROM recibos LIMIT 1");
        }

        Assert.Equal(0L, await world.CountAsync("SELECT count(*) FROM account_movements WHERE kind = 'AdjustmentDifference'"));
        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => world.Database.Context.GetService<IMigrator>().MigrateAsync(BeforeM7));

        Assert.Contains("cannot be rolled back", refused.MessageText);
    }

    [SkippableFact]
    public async Task Test67_AnExistingRecibo_ReadsIdentically_AfterM7RollsBackAndAppliesAgain()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var account = await world.SeedAccountAsync();
        await world.MaterialiseAsync(account, DeskWorld.Today);
        var paid = await DeskWorld.PayAsync(world.NewDesk(), account);
        Assert.True(paid.Succeeded, paid.Refusal?.ToString());
        var before = await LinesAsync(world);
        Assert.NotEmpty(before);

        var migrator = world.Database.Context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeM7);
        Assert.Equal(before, await LinesAsync(world));

        await migrator.MigrateAsync();
        Assert.Equal(before, await LinesAsync(world));
    }
}
