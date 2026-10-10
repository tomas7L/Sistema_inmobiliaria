using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Lazy materialisation against a real engine: idempotence, catching up after a silence, and the
/// unique-index race design Decision 1 settles with a constraint instead of a lock.
///
/// These belong here rather than in the domain suite because the behaviours that matter — writing
/// nothing the second time, and losing a race gracefully — are behaviours of the database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountMaterialiserTests
{
    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    private readonly PostgresFixture _fixture;

    public AccountMaterialiserTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// A contract starting on 1 January at a known canon, with its account. Seeded here rather
    /// than through the adjustment tests' builder because these tests need to control the start
    /// date and the canon exactly.
    /// </summary>
    private static async Task<(Contract Contract, ContractAccount Account)> SeedAsync(
        InmobiliariaDbContext context, decimal monthlyRent = 500_000m, int dueDay = 10)
    {
        var unit = new PropertyUnit(
            Guid.NewGuid(),
            new Address("Fake Street", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322"));
        context.Units.Add(unit);

        var contract = new Contract(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: monthlyRent,
            unitShares: [new UnitShare(unit.Id, 100m)],
            dueDay: dueDay);
        context.Contracts.Add(contract);

        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        context.ContractAccounts.Add(account);

        await context.SaveChangesAsync();

        return (contract, account);
    }

    private static async Task<ContractAccount> ReloadAsync(
        InmobiliariaDbContext context, Guid accountId) =>
        await context.ContractAccounts
            .Include(a => a.Movements)
            .SingleAsync(a => a.Id == accountId);

    /// <summary>
    /// Spec test 16, the write side: a period is materialised the first time anything asks, and
    /// asking again writes nothing. Idempotent, which is what lets every reader call it without
    /// coordinating.
    /// </summary>
    [SkippableFact]
    public async Task MaterialisingTwiceForTheSameDate_WritesNothingTheSecondTime()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(context);

        var materialiser = new AccountMaterialiser(context);
        var through = new DateOnly(2026, 1, 31);

        var first = await materialiser.MaterialiseDueAccrualsThroughAsync(account.Id, through);
        var second = await materialiser.MaterialiseDueAccrualsThroughAsync(account.Id, through);

        Assert.Equal(1, first);
        Assert.Equal(0, second);

        await using var fresh = _fixture.CreateDbContext();
        Assert.Single((await ReloadAsync(fresh, account.Id)).Movements);
    }

    /// <summary>
    /// It catches up. Asked after a three-month silence it writes three periods, each dated at its
    /// own due day — which is the whole reason this change needs no scheduler: a missed month is
    /// not a hole, it is just a period nobody has asked about yet.
    /// </summary>
    [SkippableFact]
    public async Task AfterAThreeMonthSilence_ItWritesThreePeriods()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(context);

        var written = await new AccountMaterialiser(context)
            .MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2026, 3, 31));

        Assert.Equal(3, written);

        await using var fresh = _fixture.CreateDbContext();
        var reloaded = await ReloadAsync(fresh, account.Id);

        Assert.Equal(
            [new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)],
            reloaded.Movements.OrderBy(m => m.Period).Select(m => m.Period));

        Assert.Equal(
            [new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 10), new DateOnly(2026, 3, 10)],
            reloaded.Movements.OrderBy(m => m.OccurredOn).Select(m => m.OccurredOn));
    }

    /// <summary>
    /// Each caught-up period is written at the canon in force for ITS OWN period, not at today's.
    /// The canon is reconstructed from the adjustment history, so an adjustment confirmed in March
    /// must not re-price January and February.
    /// </summary>
    [SkippableFact]
    public async Task CatchingUpAcrossAnAdjustment_WritesEachPeriodAtItsOwnCanon()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        // Everything is seeded in ONE unit of work, including the adjustment. That is arrangement,
        // not avoidance — the subject here is which canon each period is written at — but the
        // reason is worth naming: confirming an adjustment on an ALREADY-PERSISTED contract fails
        // today with a foreign-key violation on rent_adjustment_index_values, because
        // RentAdjustmentConfiguration lacks the `ValueGeneratedNever` that AccountMovement and
        // ContractAccount now carry. A pre-existing defect in the rent-adjustment capability,
        // reported rather than fixed here, and proven by having hit it.
        var unit = new PropertyUnit(
            Guid.NewGuid(),
            new Address("Fake Street", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322"));
        context.Units.Add(unit);

        var contract = new Contract(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(unit.Id, 100m)],
            dueDay: 10);
        context.Contracts.Add(contract);

        var index = new EconomicIndex(Guid.NewGuid(), $"IPC-{Guid.NewGuid():N}");
        context.EconomicIndices.Add(index);

        var confirmer = new AppUser(
            Guid.NewGuid(), $"mat-{Guid.NewGuid():N}"[..24], "Materialiser Test User");
        context.AppUsers.Add(confirmer);

        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        context.ContractAccounts.Add(account);

        // 100 to 120 is a 20% variation, so 500,000 becomes 600,000 from March.
        SchemaConstraintTests.ConfirmAdjustment(
            contract, index.Id, new IndexPeriod(2026, 1), 100m, new IndexPeriod(2026, 2), 120m,
            new DateOnly(2026, 3, 1), DateTimeOffset.UtcNow, confirmer.Id);

        await context.SaveChangesAsync();
        Assert.Equal(600_000m, contract.MonthlyRent);

        var written = await new AccountMaterialiser(context)
            .MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2026, 3, 31));

        Assert.Equal(3, written);

        await using var fresh = _fixture.CreateDbContext();
        var byPeriod = (await ReloadAsync(fresh, account.Id))
            .Movements.ToDictionary(m => m.Period!.Value, m => m.Amount);

        Assert.Equal(500_000m, byPeriod[new DateOnly(2026, 1, 1)]);
        Assert.Equal(500_000m, byPeriod[new DateOnly(2026, 2, 1)]);
        Assert.Equal(600_000m, byPeriod[new DateOnly(2026, 3, 1)]);
    }

    /// <summary>
    /// The premise the concurrency handling rests on: a duplicate accrual really is refused, and
    /// the error really does carry SQLSTATE 23505.
    ///
    /// Pinned explicitly because that string is what the adapter matches on. If Postgres or Npgsql
    /// ever reported something else, the catch would stop catching and a lost race would surface
    /// as an unhandled exception — and no other test would notice.
    /// </summary>
    [SkippableFact]
    public async Task ADuplicateAccrual_FailsWithTheSqlStateTheAdapterMatches()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(context);

        await new AccountMaterialiser(context)
            .MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2026, 1, 31));

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO account_movements (id, account_id, kind, amount, occurred_on, period)
                VALUES ({Guid.NewGuid()}, {account.Id}, 'RentAccrual', 500000, DATE '2026-01-10', DATE '2026-01-01')
                """));

        Assert.Equal("23505", duplicate.SqlState);
    }

    /// <summary>
    /// Design Decision 1's race, driven for real: two materialisers on separate contexts going for
    /// the same period at the same moment.
    ///
    /// What is asserted is deterministic even though the path taken is not. Exactly one accrual
    /// exists afterwards, the two calls report one write between them, and no exception escapes
    /// either — whichever of them lost. A loser either finds the period already written, or takes
    /// the unique violation and re-reads; both are correct and both return zero.
    /// </summary>
    [SkippableFact]
    public async Task TwoMaterialisersRacingForOnePeriod_WriteItExactlyOnce()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var seed = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(seed);

        var through = new DateOnly(2026, 1, 31);

        await using var first = _fixture.CreateDbContext();
        await using var second = _fixture.CreateDbContext();

        var results = await Task.WhenAll(
            new AccountMaterialiser(first).MaterialiseDueAccrualsThroughAsync(account.Id, through),
            new AccountMaterialiser(second).MaterialiseDueAccrualsThroughAsync(account.Id, through));

        Assert.Equal(1, results.Sum());

        await using var fresh = _fixture.CreateDbContext();
        Assert.Single((await ReloadAsync(fresh, account.Id)).Movements);
    }

    /// <summary>
    /// A loser leaves nothing behind. The rejected movements sit in the loaded account's
    /// collection until the tracker is cleared, and a context that kept them would hand the next
    /// read an account holding rows the database refused.
    /// </summary>
    [SkippableFact]
    public async Task AfterALostRace_TheContextHoldsNoRejectedMovements()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var seed = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(seed);

        var through = new DateOnly(2026, 1, 31);

        await using var winner = _fixture.CreateDbContext();
        await using var loser = _fixture.CreateDbContext();

        await Task.WhenAll(
            new AccountMaterialiser(winner).MaterialiseDueAccrualsThroughAsync(account.Id, through),
            new AccountMaterialiser(loser).MaterialiseDueAccrualsThroughAsync(account.Id, through));

        // Whichever context lost, neither may now be holding an Added movement.
        Assert.DoesNotContain(
            winner.ChangeTracker.Entries<AccountMovement>(), e => e.State == EntityState.Added);
        Assert.DoesNotContain(
            loser.ChangeTracker.Entries<AccountMovement>(), e => e.State == EntityState.Added);
    }

    /// <summary>
    /// Spec test 28 at the integration level: no rent accrues after the contract ended. The
    /// schedule stops producing periods, so the materialiser writes none — which is what lets a
    /// settled account carry no closed flag.
    /// </summary>
    [SkippableFact]
    public async Task AnEndedContract_MaterialisesNothingAfterItsEndDate()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contract, account) = await SeedAsync(context);

        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 2, 20));
        await context.SaveChangesAsync();

        var written = await new AccountMaterialiser(context)
            .MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2026, 12, 31));

        // January and February only; nothing for March onward.
        Assert.Equal(2, written);

        await using var fresh = _fixture.CreateDbContext();
        var periods = (await ReloadAsync(fresh, account.Id)).Movements.Select(m => m.Period);

        Assert.DoesNotContain(new DateOnly(2026, 3, 1), periods);
    }

    /// <summary>
    /// The two figures end to end: materialise, then read both. The amount owed carries the
    /// surcharge the ledger balance deliberately does not.
    /// </summary>
    [SkippableFact]
    public async Task AfterMaterialising_TheTwoFiguresDifferByTheProjectedRecargo()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(context);

        var asOf = new DateOnly(2026, 1, 20);
        await new AccountMaterialiser(context).MaterialiseDueAccrualsThroughAsync(account.Id, asOf);

        await using var fresh = _fixture.CreateDbContext();
        var reloaded = await ReloadAsync(fresh, account.Id);

        // Due the 10th, read the 20th: ten days at 2% of 500,000.
        Assert.Equal(500_000m, reloaded.LedgerBalance(asOf));
        Assert.Equal(600_000m, reloaded.AmountOwed(asOf, LeaseTerms));
    }

    [SkippableFact]
    public async Task MaterialisingAnAccountThatDoesNotExist_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AccountMaterialiser(context)
                .MaterialiseDueAccrualsThroughAsync(Guid.NewGuid(), new DateOnly(2026, 1, 31)));
    }
}
