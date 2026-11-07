using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>gestion-de-alquileres spec Tests 35 to 37 and 56 through <see cref="DueAdjustmentQuery"/>, against real Postgres.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ClauseWindowQueryTests
{
    private static readonly DateOnly DueJuly = new(2026, 7, 1);

    private readonly PostgresFixture _fixture;

    public ClauseWindowQueryTests(PostgresFixture fixture) => _fixture = fixture;

    private static (Contract Contract, EconomicIndex First, EconomicIndex Second) Seed(
        InmobiliariaDbContext context, int offset, bool twoIndices = false, DateOnly? start = null)
    {
        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Alem", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322"));
        context.Units.Add(unit);

        var starts = start ?? new DateOnly(2026, 1, 1);
        var contract = new Contract(Guid.NewGuid(), starts, starts.AddYears(4), 100_000m, [new UnitShare(unit.Id, 100m)]);
        context.Contracts.Add(contract);

        var first = new EconomicIndex(Guid.NewGuid(), $"A-{Guid.NewGuid():N}");
        var second = new EconomicIndex(Guid.NewGuid(), $"B-{Guid.NewGuid():N}");
        context.EconomicIndices.AddRange(first, second);

        var clause = new AdjustmentClause(
            Guid.NewGuid(), contract.Id, twoIndices ? [first.Id, second.Id] : [first.Id], 6, RoundingRule.TruncateToWholePeso, offset);
        context.AdjustmentClauses.Add(clause);
        contract.AttachAdjustmentClause(clause);

        return (contract, first, second);
    }

    private static async Task<DueAdjustment> DueOfAsync(InmobiliariaDbContext context, Contract contract, DateOnly? asOf = null) =>
        Assert.Single(await new DueAdjustmentQuery(context).GetDueAsync(asOf ?? DueJuly), d => d.ContractId == contract.Id);

    // ---- Tests 35 and 36 ------------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(0, 2026, 1, 2026, 7)]
    [InlineData(1, 2025, 12, 2026, 6)]
    [InlineData(2, 2025, 11, 2026, 5)]
    [InlineData(3, 2025, 10, 2026, 4)]
    public async Task EachOffset_ReadsItsPeriods_AndNamesThoseAsMissing(int offset, int baseYear, int baseMonth, int endYear, int endMonth)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, index, _) = Seed(context, offset);
        await context.SaveChangesAsync();

        var due = await DueOfAsync(context, contract);

        var basePeriod = new IndexPeriod(baseYear, baseMonth);
        var endPeriod = new IndexPeriod(endYear, endMonth);
        Assert.Equal(basePeriod, due.BasePeriod);
        Assert.Equal(endPeriod, due.EndPeriod);
        Assert.Equal(
            [(index.Id, basePeriod), (index.Id, endPeriod)],
            due.Missing.Select(m => (m.EconomicIndexId, m.Period)));
    }

    /// <summary>The trap of a default on the model: EF takes 0 as "not set", and ICL would silently read as offset 1.</summary>
    [SkippableFact]
    public async Task OffsetZero_IsStoredAsZero()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, _, _) = Seed(context, offset: 0);
        await context.SaveChangesAsync();

        await using var read = _fixture.CreateDbContext();

        Assert.Equal(0, (await read.AdjustmentClauses.AsNoTracking().SingleAsync(c => c.ContractId == contract.Id)).WindowOffsetMonths);
    }

    /// <summary>The trap of the scaffold's default of 0, which would have moved every clause already there.</summary>
    [SkippableFact]
    public async Task AClauseRecordedBeforeThisChange_BehavesAsOffsetOne()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, index, _) = Seed(context, offset: 2);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlAsync($"DELETE FROM adjustment_clauses WHERE contract_id = {contract.Id}");
        var clauseId = Guid.NewGuid();
        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO adjustment_clauses (id, contract_id, interval_months, rounding_rule) VALUES ({clauseId}, {contract.Id}, 6, 'TruncateToWholePeso')");
        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO adjustment_clause_indices (adjustment_clause_id, economic_index_id, ordinal) VALUES ({clauseId}, {index.Id}, 0)");

        await using var read = _fixture.CreateDbContext();
        var due = await DueOfAsync(read, contract);

        Assert.Equal(new IndexPeriod(2026, 6), due.EndPeriod);
        Assert.Equal(new IndexPeriod(2025, 12), due.BasePeriod);
    }

    [SkippableTheory]
    [InlineData(4)]
    [InlineData(-1)]
    public async Task TheDatabase_RefusesAnOffsetOutOfRange(int offset)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, _, _) = Seed(context, offset: 1);
        await context.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"UPDATE adjustment_clauses SET window_offset_months = {offset} WHERE contract_id = {contract.Id}"));

        Assert.Equal("ck_adjustment_clauses_window_offset_range", refused.ConstraintName);
    }

    // ---- Test 37 --------------------------------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AnAverageWithOneIndexMissing_StaysPending_AtAnyOffset(int offset)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, first, second) = Seed(context, offset, twoIndices: true);
        var window = AdjustmentWindow.For(DueJuly, 6, offset);
        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), first.Id, window.Base, 100m));
        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), first.Id, window.End, 110m));
        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), second.Id, window.Base, 200m));
        await context.SaveChangesAsync();

        var due = await DueOfAsync(context, contract);

        var missing = Assert.Single(due.Missing);
        Assert.Equal((second.Id, window.End), (missing.EconomicIndexId, missing.Period));
        Assert.Null(due.Coefficient);
        Assert.Null(due.ProposedCanon);
        Assert.Empty(due.IndexValues);
    }

    [SkippableFact]
    public async Task WithEveryValueThere_TheProposalCarriesEachIndexsLevels()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, index, _) = Seed(context, offset: 0);
        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 1), 100m));
        context.IndexValues.Add(new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 7), 110m));
        await context.SaveChangesAsync();

        var due = await DueOfAsync(context, contract);

        Assert.Empty(due.Missing);
        Assert.Equal(110_000m, due.ProposedCanon);
        var levels = Assert.Single(due.IndexValues);
        Assert.Equal((100m, 110m), (levels.BaseLevel, levels.EndLevel));
    }

    // ---- Test 56 --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task APreExistingContract_IsNotListedBeforeTheFirstStepAfterItsCutover()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, _, _) = Seed(context, offset: 1, start: new DateOnly(2024, 9, 1));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlAsync($"UPDATE contracts SET cutover_date = {new DateOnly(2026, 11, 5)} WHERE id = {contract.Id}");

        await using var read = _fixture.CreateDbContext();
        var query = new DueAdjustmentQuery(read);

        Assert.DoesNotContain(await query.GetDueAsync(new DateOnly(2027, 2, 28)), d => d.ContractId == contract.Id);
        Assert.Equal(new DateOnly(2027, 3, 1), (await DueOfAsync(read, contract, new DateOnly(2027, 3, 1))).DueDate);
    }

    [SkippableFact]
    public async Task WithoutACutover_TheOldestStepIsListed()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, _, _) = Seed(context, offset: 1, start: new DateOnly(2024, 9, 1));
        await context.SaveChangesAsync();

        Assert.Equal(new DateOnly(2025, 3, 1), (await DueOfAsync(context, contract, new DateOnly(2027, 3, 1))).DueDate);
    }

    [SkippableFact]
    public async Task AConfirmedAdjustment_MovesTheWindowOnByTheInterval_AtTheClausesOffset()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var userId = await AccessTestSupport.ProvisionUserAsync(context, $"ventana-{Guid.NewGuid():N}"[..20], UserRole.Admin);
        var (contract, index, _) = Seed(context, offset: 2);
        await context.SaveChangesAsync();
        SchemaConstraintTests.ConfirmAdjustment(
            contract, index.Id, new IndexPeriod(2025, 11), 100m, new IndexPeriod(2026, 5), 110m, DueJuly, DateTimeOffset.UtcNow, userId);
        await context.SaveChangesAsync();

        var due = await DueOfAsync(context, contract, new DateOnly(2027, 1, 1));

        Assert.Equal(new DateOnly(2027, 1, 1), due.DueDate);
        Assert.Equal(new IndexPeriod(2026, 5), due.BasePeriod);
        Assert.Equal(new IndexPeriod(2026, 11), due.EndPeriod);
    }
}
