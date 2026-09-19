using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Proves <see cref="DueAdjustmentQuery"/> against a real Postgres engine (design.md Decision
/// 8): the reminder-ready worklist that names which index and period is missing rather than
/// firing a blind monthly alert (spec "The Pending Worklist Is Reminder-Ready").
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DueAdjustmentQueryTests
{
    private readonly PostgresFixture _fixture;

    public DueAdjustmentQueryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static Address TestAddress(string number) =>
        new("Fake Street", number, "Springfield", "Buenos Aires", "1000");

    /// <summary>
    /// Seeds a unit-backed, Active contract with a single-index, one-month-interval clause,
    /// started exactly one month before <paramref name="dueDate"/> so the derived next due date
    /// lands on it — with no confirmed adjustment yet, the due date is StartDate + interval.
    /// </summary>
    private static (Contract Contract, EconomicIndex Index) SeedDueContract(
        InmobiliariaDbContext context, DateOnly dueDate, decimal monthlyRent = 450_000m)
    {
        var unit = new PropertyUnit(Guid.NewGuid(), TestAddress(Guid.NewGuid().ToString("N")[..6]));
        context.Units.Add(unit);

        var startDate = dueDate.AddMonths(-1);
        var contract = new Contract(
            Guid.NewGuid(), startDate, startDate.AddYears(2), monthlyRent, [new UnitShare(unit.Id, 100m)]);
        context.Contracts.Add(contract);

        var index = new EconomicIndex(Guid.NewGuid(), $"IPC-{Guid.NewGuid():N}");
        context.EconomicIndices.Add(index);

        var clause = new AdjustmentClause(
            Guid.NewGuid(), contract.Id, [index.Id], intervalMonths: 1, RoundingRule.TruncateToWholePeso);
        context.AdjustmentClauses.Add(clause);
        contract.AttachAdjustmentClause(clause);

        return (contract, index);
    }

    [SkippableFact]
    public async Task ContractWithNoAdjustmentClause_NeverAppearsInGetDueAsync()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var unit = new PropertyUnit(Guid.NewGuid(), TestAddress("700"));
        context.Units.Add(unit);
        var fixedPriceContract = new Contract(
            Guid.NewGuid(), new DateOnly(2020, 1, 1), new DateOnly(2099, 12, 31), 100_000m,
            [new UnitShare(unit.Id, 100m)]);
        context.Contracts.Add(fixedPriceContract);
        await context.SaveChangesAsync();

        var due = await new DueAdjustmentQuery(context).GetDueAsync(new DateOnly(2099, 1, 1));

        Assert.DoesNotContain(due, d => d.ContractId == fixedPriceContract.Id);
    }

    [SkippableFact]
    public async Task ThreeContractsAwaitingIpc_AreReturnedNamingTheMissingIndexAndPeriod()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var dueDate = new DateOnly(2026, 9, 1);
        var seeded = new List<(Contract Contract, EconomicIndex Index)>
        {
            SeedDueContract(context, dueDate),
            SeedDueContract(context, dueDate),
            SeedDueContract(context, dueDate),
        };

        // The base period, 2026-07, is published; the end period, 2026-08, is not — the exact
        // publish-lag situation design.md Decision 8 names as the ordinary path, not the
        // exception.
        foreach (var (_, index) in seeded)
        {
            context.IndexValues.Add(new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 7), 8_000m));
        }

        await context.SaveChangesAsync();

        var due = await new DueAdjustmentQuery(context).GetDueAsync(dueDate);

        foreach (var (contract, index) in seeded)
        {
            var entry = Assert.Single(due, d => d.ContractId == contract.Id);

            Assert.Null(entry.Coefficient);
            Assert.Null(entry.ProposedCanon);
            Assert.Contains(
                entry.Missing, m => m.EconomicIndexId == index.Id && m.Period == new IndexPeriod(2026, 8));
        }
    }

    [SkippableFact]
    public async Task MissingIndexValue_LeavesCoefficientAndProposedCanonNullAndCanonUnaffected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var dueDate = new DateOnly(2026, 9, 1);
        var (contract, index) = SeedDueContract(context, dueDate, monthlyRent: 450_000m);
        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 7), 8_000m));
        await context.SaveChangesAsync();

        var due = await new DueAdjustmentQuery(context).GetDueAsync(dueDate);
        var entry = Assert.Single(due, d => d.ContractId == contract.Id);

        Assert.Null(entry.Coefficient);
        Assert.Null(entry.ProposedCanon);
        Assert.NotEmpty(entry.Missing);
        Assert.Equal(450_000m, entry.CurrentCanon);
        Assert.Equal(450_000m, contract.MonthlyRent);

        // The invariant design.md Decision 8 states explicitly (task 4.8).
        Assert.True((entry.Coefficient is null) == (entry.Missing.Count > 0));
    }

    [SkippableFact]
    public async Task OnceTheMissingValueArrives_NoneOfTheContractsWaitsOnAMissingValue()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var dueDate = new DateOnly(2026, 9, 1);
        var seeded = new List<(Contract Contract, EconomicIndex Index)>
        {
            SeedDueContract(context, dueDate),
            SeedDueContract(context, dueDate),
            SeedDueContract(context, dueDate),
        };

        foreach (var (_, index) in seeded)
        {
            context.IndexValues.Add(new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 7), 8_000m));
        }

        await context.SaveChangesAsync();

        // The IPC value for the previously missing period, 2026-08, is entered.
        foreach (var (_, index) in seeded)
        {
            context.IndexValues.Add(new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 8), 9_440m));
        }

        await context.SaveChangesAsync();

        var due = await new DueAdjustmentQuery(context).GetDueAsync(dueDate);

        foreach (var (contract, _) in seeded)
        {
            var entry = Assert.Single(due, d => d.ContractId == contract.Id);

            Assert.Empty(entry.Missing);
            Assert.Equal(18m, entry.Coefficient);
            Assert.Equal(531_000m, entry.ProposedCanon);
        }
    }
}
