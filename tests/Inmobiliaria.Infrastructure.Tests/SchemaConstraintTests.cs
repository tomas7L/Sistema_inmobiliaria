using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Proves the database-level backstop of design.md Decision 3 and the six EF mappings from
/// Slice 2 against a real Postgres engine — constraints an in-memory provider cannot enforce.
/// Every fact skips (not fails) when Docker is unreachable locally; the ubuntu-latest CI job
/// always runs them (design.md Decision 4).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaConstraintTests
{
    private readonly PostgresFixture _fixture;

    public SchemaConstraintTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static Address TestAddress(string number) =>
        new("Fake Street", number, "Springfield", "Buenos Aires", "1000");

    private static Contract NewContract(
        Guid unitId,
        decimal monthlyRent = 100_000m,
        decimal? honorariosPercentage = null) =>
        new(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent,
            [new UnitShare(unitId, 100m)],
            honorariosPercentage);

    /// <summary>
    /// Creates a unit, registers it with <paramref name="context"/>, and returns a contract
    /// covering it at 100%. A contract cannot exist without a unit, and contract_units carries
    /// a foreign key to units, so every persisted contract needs a real unit row behind it.
    /// </summary>
    private static Contract NewContractWithUnit(
        InmobiliariaDbContext context,
        decimal monthlyRent = 100_000m,
        decimal? honorariosPercentage = null)
    {
        var unit = new PropertyUnit(Guid.NewGuid(), TestAddress("1"));
        context.Units.Add(unit);

        return NewContract(unit.Id, monthlyRent, honorariosPercentage);
    }

    /// <summary>
    /// Seeds a contract with a single-index, `intervalMonths`-interval adjustment clause,
    /// registering everything against <paramref name="context"/> but not yet saving.
    /// </summary>
    private static (Contract Contract, EconomicIndex Index) SeedContractWithClause(
        InmobiliariaDbContext context,
        int intervalMonths = 6,
        decimal monthlyRent = 450_000m)
    {
        var unit = new PropertyUnit(Guid.NewGuid(), TestAddress(Guid.NewGuid().ToString("N")[..6]));
        context.Units.Add(unit);

        var contract = NewContract(unit.Id, monthlyRent);
        context.Contracts.Add(contract);

        var index = new EconomicIndex(Guid.NewGuid(), $"IPC-{Guid.NewGuid():N}");
        context.EconomicIndices.Add(index);

        var clause = new AdjustmentClause(
            Guid.NewGuid(), contract.Id, [index.Id], intervalMonths, RoundingRule.TruncateToWholePeso);
        context.AdjustmentClauses.Add(clause);
        contract.AttachAdjustmentClause(clause);

        return (contract, index);
    }

    /// <summary>
    /// Confirms a single-index adjustment through the real domain pipeline —
    /// <see cref="AdjustmentProposal"/> then <see cref="Contract.ConfirmAdjustment"/> — using the
    /// average-of-variations formula directly, so the stored coefficient is independently
    /// verifiable against a hand-computed expectation in the tests below.
    /// </summary>
    private static RentAdjustment ConfirmAdjustment(
        Contract contract,
        Guid indexId,
        IndexPeriod basePeriod,
        decimal baseLevel,
        IndexPeriod endPeriod,
        decimal endLevel,
        DateOnly effectiveDate,
        DateTimeOffset confirmedAt,
        AdjustmentKind kind = AdjustmentKind.Regular,
        Guid? correctsAdjustmentId = null)
    {
        var variation = decimal.Round(((endLevel / baseLevel) - 1m) * 100m, 6);
        var snapshot = new RentAdjustmentIndexValue(indexId, indexId, basePeriod, baseLevel, endPeriod, endLevel, variation);
        var proposal = new AdjustmentProposal(
            contract.MonthlyRent, [snapshot], CombinationRule.Single, variation, effectiveDate);

        return contract.ConfirmAdjustment(Guid.NewGuid(), proposal, confirmedAt, kind, correctsAdjustmentId);
    }

    [SkippableFact]
    public async Task NumericColumnPrecision_MatchesMoneyAndIndexLevelConventions()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        async Task<(int Precision, int Scale)> ColumnPrecisionAsync(string table, string column)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT numeric_precision, numeric_scale FROM information_schema.columns
                WHERE table_name = @table AND column_name = @column
                """;
            command.Parameters.Add(new NpgsqlParameter("table", table));
            command.Parameters.Add(new NpgsqlParameter("column", column));

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"Column {table}.{column} was not found.");

            return (reader.GetInt32(0), reader.GetInt32(1));
        }

        // Canon columns: numeric(14,2), the money convention (spec test 6, integration half).
        Assert.Equal((14, 2), await ColumnPrecisionAsync("rent_adjustments", "previous_canon"));
        Assert.Equal((14, 2), await ColumnPrecisionAsync("rent_adjustments", "new_canon"));

        // Level columns: numeric(18,6). An index level is not money (design.md Decision 1).
        Assert.Equal((18, 6), await ColumnPrecisionAsync("index_values", "level"));
        Assert.Equal((18, 6), await ColumnPrecisionAsync("rent_adjustment_index_values", "base_level"));
        Assert.Equal((18, 6), await ColumnPrecisionAsync("rent_adjustment_index_values", "end_level"));
    }

    [SkippableFact]
    public async Task AppendOnlyTrigger_RejectsRawUpdateAndDelete()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var (contract, index) = SeedContractWithClause(context);
        var adjustment = ConfirmAdjustment(
            contract, index.Id,
            new IndexPeriod(2026, 1), 8_000m,
            new IndexPeriod(2026, 7), 9_440m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow);

        await context.SaveChangesAsync();

        // Raw SQL deliberately bypasses the change tracker and RentAdjustment's own lack of a
        // public mutator: this proves the DATABASE trigger — not the domain — rejects the
        // mutation at the database level (spec test 13).
        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE rent_adjustments SET coefficient = 99 WHERE id = {adjustment.Id}"));

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM rent_adjustments WHERE id = {adjustment.Id}"));
    }

    [SkippableFact]
    public async Task CorrectingAnIndexValue_LeavesTheConfirmedAdjustmentUnchangedAndAppendsACorrection()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var (contract, index) = SeedContractWithClause(context);

        var basePeriod = new IndexPeriod(2026, 1);
        var endPeriod = new IndexPeriod(2026, 7);

        var indexValue = new IndexValue(Guid.NewGuid(), index.Id, endPeriod, 9_440m);
        context.IndexValues.Add(indexValue);

        var original = ConfirmAdjustment(
            contract, index.Id, basePeriod, 8_000m, endPeriod, 9_440m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow);

        await context.SaveChangesAsync();

        // The correction: IndexValue.Correct mutates the published level in place (spec
        // "A Correction Produces a New Adjustment, Never a Rewrite") — the confirmed
        // RentAdjustment above must remain exactly as it was.
        indexValue.Correct(9_400m);

        var correction = ConfirmAdjustment(
            contract, index.Id, basePeriod, 8_000m, endPeriod, 9_400m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow,
            AdjustmentKind.Correction, original.Id);

        // EF Core gotcha, load-bearing here: `correction` was appended to `contract`'s
        // already-tracked `_adjustments` field by domain code alone (Contract has zero EF
        // references by design), not through `context.Add(...)`. A newly-appeared entity
        // discovered only via DetectChanges' collection diff, carrying a non-default
        // client-assigned Guid key, is NOT automatically inferred as Added — EF has no way to
        // tell it apart from a row that already exists — so without this explicit call the
        // second SaveChangesAsync silently omits the INSERT for `correction` itself while
        // still attempting to insert its child rent_adjustment_index_values row, which then
        // fails its foreign key. Any future application layer confirming a second adjustment
        // against an already-tracked Contract needs this same explicit call.
        context.RentAdjustments.Add(correction);

        await context.SaveChangesAsync();

        await using var readContext = _fixture.CreateDbContext();
        var rows = await readContext.RentAdjustments
            .Where(a => a.ContractId == contract.Id)
            .ToListAsync();

        Assert.Equal(2, rows.Count);

        var reloadedOriginal = rows.Single(a => a.Id == original.Id);
        Assert.Equal(AdjustmentKind.Regular, reloadedOriginal.Kind);
        Assert.Null(reloadedOriginal.CorrectsAdjustmentId);
        Assert.Equal(18m, reloadedOriginal.Coefficient);

        var reloadedCorrection = rows.Single(a => a.Id == correction.Id);
        Assert.Equal(AdjustmentKind.Correction, reloadedCorrection.Kind);
        Assert.Equal(original.Id, reloadedCorrection.CorrectsAdjustmentId);
        Assert.Equal(17.5m, reloadedCorrection.Coefficient);
    }

    [SkippableFact]
    public async Task DeferredTrigger_AllowsValidTwoUnitSplitInsertedRowByRow()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var unitA = new PropertyUnit(Guid.NewGuid(), TestAddress("100"));
        var unitB = new PropertyUnit(Guid.NewGuid(), TestAddress("200"));
        context.Units.AddRange(unitA, unitB);

        var contract = new Contract(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            100_000m,
            [new UnitShare(unitA.Id, 60m), new UnitShare(unitB.Id, 40m)]);
        context.Contracts.Add(contract);

        // EF inserts the two contract_units rows one at a time inside this single
        // SaveChanges transaction; only DEFERRABLE INITIALLY DEFERRED lets this commit.
        await context.SaveChangesAsync();

        var persisted = await context.ContractUnits
            .Where(cu => cu.ContractId == contract.Id)
            .ToListAsync();

        Assert.Equal(2, persisted.Count);
        Assert.Equal(100m, persisted.Sum(cu => cu.SharePercentage));
    }

    [SkippableFact]
    public async Task DeferredTrigger_RejectsCommittedSplitNotSummingTo100()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var strayUnit = new PropertyUnit(Guid.NewGuid(), TestAddress("300"));
        context.Units.Add(strayUnit);
        var contract = NewContractWithUnit(context);
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();

        // Raw insert deliberately bypasses Contract.SetUnitShares: this proves the DATABASE
        // trigger — not the domain invariant — rejects a split that does not total 100%.
        // The contract's own unit already holds 100%, so adding a stray unit at 60% takes
        // it to 160% and the deferred trigger must reject the commit.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO contract_units (contract_id, unit_id, share_percentage)
            VALUES ({contract.Id}, {strayUnit.Id}, 60)
            """);

        await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
    }

    [SkippableFact]
    public async Task DuplicateContractUnitCompositeKey_Rejected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var unit = new PropertyUnit(Guid.NewGuid(), TestAddress("400"));
        context.Units.Add(unit);

        var contract = NewContract(unit.Id);
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        // Raw insert deliberately bypasses the change tracker. EF refuses to track two entities
        // sharing a key and throws in memory, so going through SaveChanges would never reach
        // Postgres and would prove nothing about the composite primary key on contract_units.
        // The domain rejects this case first (Contract.SetUnitShares); this asserts the
        // database backstop still holds for anything that reaches it by another path.
        var insertDuplicate = () => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO contract_units (contract_id, unit_id, share_percentage)
            VALUES ({contract.Id}, {unit.Id}, 100)
            """);

        var error = await Assert.ThrowsAsync<PostgresException>(insertDuplicate);

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [SkippableFact]
    public async Task DuplicateContractPartyCompositeKey_Rejected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var party = new Party(Guid.NewGuid(), "Ana", "Gomez");
        context.Parties.Add(party);
        var contract = NewContractWithUnit(context);
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO contract_parties (contract_id, party_id, role) VALUES ({contract.Id}, {party.Id}, 'Codebtor')");

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO contract_parties (contract_id, party_id, role) VALUES ({contract.Id}, {party.Id}, 'Codebtor')"));
    }

    [SkippableFact]
    public async Task InvalidRoleCheckConstraint_Rejected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var party = new Party(Guid.NewGuid(), "Bad", "Role");
        context.Parties.Add(party);
        var contract = NewContractWithUnit(context);
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        // 'Garante' must never be a legal role value (lease-contract: Codebtor Terminology).
        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO contract_parties (contract_id, party_id, role) VALUES ({contract.Id}, {party.Id}, 'Garante')"));
    }

    [SkippableFact]
    public async Task InvalidStatusCheckConstraint_Rejected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var contract = NewContractWithUnit(context);
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        // 'Draft' must never be a legal status (lease-contract: No Draft State).
        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE contracts SET status = 'Draft' WHERE id = {contract.Id}"));
    }

    [SkippableFact]
    public async Task InvalidDocumentKindCheckConstraint_Rejected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var contract = NewContractWithUnit(context);
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO contract_documents
                    (id, contract_id, storage_path, file_name, content_type, uploaded_at, uploaded_by, kind)
                VALUES
                    ({Guid.NewGuid()}, {contract.Id}, 'path', 'file.pdf', 'application/pdf',
                     {DateTimeOffset.UtcNow}, 'tester', 'Draft')
                """));
    }

    [SkippableFact]
    public async Task TphRoundTrip_PreservesConcreteUnitType()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        var propertyId = Guid.NewGuid();
        var parkingId = Guid.NewGuid();

        await using (var writeContext = _fixture.CreateDbContext())
        {
            writeContext.Units.Add(new PropertyUnit(propertyId, TestAddress("500")));
            writeContext.Units.Add(new ParkingUnit(parkingId, TestAddress("501")));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateDbContext();
        var property = await readContext.Units.SingleAsync(u => u.Id == propertyId);
        var parking = await readContext.Units.SingleAsync(u => u.Id == parkingId);

        Assert.IsType<PropertyUnit>(property);
        Assert.IsType<ParkingUnit>(parking);
    }

    [SkippableFact]
    public async Task TwoSequentialContractsOnSameUnit_BothRemainReadable()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var unit = new PropertyUnit(Guid.NewGuid(), TestAddress("600"));
        context.Units.Add(unit);

        var expiredContract = new Contract(
            Guid.NewGuid(), new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31), 80_000m,
            [new UnitShare(unit.Id, 100m)]);

        var currentContract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), 120_000m,
            [new UnitShare(unit.Id, 100m)]);

        context.Contracts.AddRange(expiredContract, currentContract);
        await context.SaveChangesAsync();

        var rowsForUnit = await context.ContractUnits
            .Where(cu => cu.UnitId == unit.Id)
            .ToListAsync();

        Assert.Equal(2, rowsForUnit.Count);
    }

    [SkippableFact]
    public async Task OriginalAndAddendumDocuments_CoexistForOneContract()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var contract = NewContractWithUnit(context);
        context.Contracts.Add(contract);

        context.ContractDocuments.AddRange(
            new ContractDocument(
                Guid.NewGuid(), contract.Id, "leases/original.pdf", "original.pdf",
                "application/pdf", DateTimeOffset.UtcNow, "agent@example.com", DocumentKind.Original),
            new ContractDocument(
                Guid.NewGuid(), contract.Id, "leases/addendum-1.pdf", "addendum-1.pdf",
                "application/pdf", DateTimeOffset.UtcNow, "agent@example.com", DocumentKind.Addendum));

        await context.SaveChangesAsync();

        var documents = await context.ContractDocuments
            .Where(d => d.ContractId == contract.Id)
            .ToListAsync();

        Assert.Equal(2, documents.Count);
        Assert.Contains(documents, d => d.Kind == DocumentKind.Original);
        Assert.Contains(documents, d => d.Kind == DocumentKind.Addendum);
    }

    [SkippableFact]
    public async Task DuplicateDni_RejectedByUniqueIndex()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var dni = $"dni-{Guid.NewGuid():N}";

        context.Parties.AddRange(
            new Party(Guid.NewGuid(), "Uno", "Apellido", dni: dni),
            new Party(Guid.NewGuid(), "Dos", "Apellido", dni: dni));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task DuplicateCuil_RejectedByUniqueIndex()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var cuil = $"cuil-{Guid.NewGuid():N}";

        context.Parties.AddRange(
            new Party(Guid.NewGuid(), "Uno", "Apellido", cuil: cuil),
            new Party(Guid.NewGuid(), "Dos", "Apellido", cuil: cuil));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
