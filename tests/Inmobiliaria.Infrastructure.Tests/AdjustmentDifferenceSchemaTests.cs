using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// M7 <c>AddAdjustmentDifference</c> (gestion-de-alquileres spec Requirement 19, Test 61, design D6): the new kind is
/// append-only, positive and period-bound, and an adjustment's recorded choice holds together, whatever writes it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdjustmentDifferenceSchemaTests
{
    private static readonly DateOnly November = new(2026, 11, 1);
    private static readonly DateOnly DueOn = new(2026, 11, 10);

    private readonly PostgresFixture _fixture;

    public AdjustmentDifferenceSchemaTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<(DeskWorld World, Guid Account)> AccountAsync()
    {
        var world = await DeskWorld.CreateAsync(_fixture);
        return (world, await world.SeedAccountAsync());
    }

    private static Task InsertAsync(InmobiliariaDbContext context, Guid account, string kind, decimal amount, DateOnly? period) =>
        context.Database.ExecuteSqlAsync($"""
            INSERT INTO account_movements (id, account_id, kind, amount, occurred_on, period)
            VALUES ({Guid.NewGuid()}, {account}, {kind}, {amount}, {DueOn}, {period})
            """);

    [SkippableFact]
    public async Task Test61_TheCharge_IsAccepted_AndCannotBeUpdatedOrDeleted()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        var (world, account) = await AccountAsync();
        await using var _ = world;
        await using var context = world.NewContext();
        await InsertAsync(context, account, "AdjustmentDifference", 300_000m, November);

        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"UPDATE account_movements SET amount = 1 WHERE account_id = {account} AND kind = 'AdjustmentDifference'"));
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"DELETE FROM account_movements WHERE account_id = {account} AND kind = 'AdjustmentDifference'"));

        Assert.Equal(1, await world.CountAsync(
            "SELECT count(*) FROM account_movements WHERE account_id = @a AND kind = 'AdjustmentDifference' AND amount = 300000",
            ("a", account)));
    }

    [SkippableTheory]
    [InlineData(0, "ck_account_movements_adjustment_difference_sign")]
    [InlineData(-300000, "ck_account_movements_adjustment_difference_sign")]
    public async Task Test61_AChargeThatIsNotPositive_IsRefused(int amount, string constraint)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        var (world, account) = await AccountAsync();
        await using var _ = world;
        await using var context = world.NewContext();

        var refused = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, account, "AdjustmentDifference", amount, November));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal(constraint, refused.ConstraintName);
    }

    [SkippableFact]
    public async Task Test61_AChargeWithoutAPeriod_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        var (world, account) = await AccountAsync();
        await using var _ = world;
        await using var context = world.NewContext();

        var refused = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, account, "AdjustmentDifference", 300_000m, null));

        Assert.Equal("ck_account_movements_period_required", refused.ConstraintName);
    }

    [SkippableFact]
    public async Task Test61_AKindOutsideTheCheckList_IsStillRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        var (world, account) = await AccountAsync();
        await using var _ = world;
        await using var context = world.NewContext();

        var refused = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, account, "Difference", 300_000m, November));

        Assert.Equal("ck_account_movements_kind", refused.ConstraintName);
    }

    [SkippableFact]
    public async Task TheRecordedChoice_RefusesAChargeWithoutAnOffer_AndAnOfferThatIsNotPositive()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var user = await AccessTestSupport.ProvisionUserAsync(context, $"m7-{Guid.NewGuid():N}"[..20], Domain.Access.UserRole.Admin);
        var (contract, index) = SchemaConstraintTests.SeedContractWithClause(context);
        await context.SaveChangesAsync();

        Task Insert(decimal? offered, Guid? charged) => context.Database.ExecuteSqlAsync($"""
            INSERT INTO rent_adjustments (id, contract_id, effective_date, previous_canon, new_canon, coefficient, kind, confirmed_at, confirmed_by, difference_amount, difference_movement_id)
            VALUES ({Guid.NewGuid()}, {contract.Id}, {new DateOnly(2026, 7, 1)}, 100000, 110000, 10, 'Regular', now(), {user}, {offered}, {charged})
            """);

        var notOffered = await Assert.ThrowsAsync<PostgresException>(() => Insert(null, Guid.NewGuid()));
        var notPositive = await Assert.ThrowsAsync<PostgresException>(() => Insert(0m, null));

        Assert.Equal("ck_rent_adjustments_difference_charged_was_offered", notOffered.ConstraintName);
        Assert.Equal("ck_rent_adjustments_difference_amount_positive", notPositive.ConstraintName);
        _ = index;
    }
}
