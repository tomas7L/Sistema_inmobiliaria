using Inmobiliaria.Domain.Access;
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
    /// <summary>
    /// Reused directly by <c>RolePermissionTests</c> (spec tests 26, 33), so this stays
    /// <see langword="internal"/> rather than <see langword="private"/> — the task explicitly
    /// calls for reusing this exact helper instead of duplicating a second seeding path.
    /// </summary>
    internal static (Contract Contract, EconomicIndex Index) SeedContractWithClause(
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
    /// Seeds a real <see cref="AppUser"/> row, registered against <paramref name="context"/>
    /// but not yet saved. users-and-roles' FK from <c>rent_adjustments.confirmed_by</c> and
    /// <c>contract_documents.uploaded_by_user_id</c> to <c>app_users</c> means a random,
    /// never-persisted <see cref="Guid"/> is no longer a valid stand-in for "some user" once
    /// the AddUsersAndRoles migration is applied — every reference used against a real
    /// Postgres engine needs a real row behind it.
    /// </summary>
    private static AppUser SeedAppUser(InmobiliariaDbContext context, string usernamePrefix = "tester")
    {
        var user = new AppUser(
            Guid.NewGuid(), $"{usernamePrefix}-{Guid.NewGuid():N}"[..24], "Schema Constraint Test User");
        context.AppUsers.Add(user);
        return user;
    }

    /// <summary>
    /// Confirms a single-index adjustment through the real domain pipeline —
    /// <see cref="AdjustmentProposal"/> then <see cref="Contract.ConfirmAdjustment"/> — using the
    /// average-of-variations formula directly, so the stored coefficient is independently
    /// verifiable against a hand-computed expectation in the tests below.
    /// </summary>
    /// <summary>Reused directly by <c>RolePermissionTests</c> (spec tests 26, 33) — see the note on <see cref="SeedContractWithClause"/>.</summary>
    internal static RentAdjustment ConfirmAdjustment(
        Contract contract,
        Guid indexId,
        IndexPeriod basePeriod,
        decimal baseLevel,
        IndexPeriod endPeriod,
        decimal endLevel,
        DateOnly effectiveDate,
        DateTimeOffset confirmedAt,
        Guid confirmedByUserId,
        AdjustmentKind kind = AdjustmentKind.Regular,
        Guid? correctsAdjustmentId = null)
    {
        var variation = decimal.Round(((endLevel / baseLevel) - 1m) * 100m, 6);
        var snapshot = new RentAdjustmentIndexValue(indexId, indexId, basePeriod, baseLevel, endPeriod, endLevel, variation);
        var proposal = new AdjustmentProposal(
            contract.MonthlyRent, [snapshot], CombinationRule.Single, variation, effectiveDate);

        return contract.ConfirmAdjustment(
            Guid.NewGuid(), proposal, confirmedAt, confirmedByUserId, kind, correctsAdjustmentId);
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
        var confirmingUser = SeedAppUser(context);
        var adjustment = ConfirmAdjustment(
            contract, index.Id,
            new IndexPeriod(2026, 1), 8_000m,
            new IndexPeriod(2026, 7), 9_440m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, confirmingUser.Id);

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
        var confirmingUser = SeedAppUser(context);

        var basePeriod = new IndexPeriod(2026, 1);
        var endPeriod = new IndexPeriod(2026, 7);

        var indexValue = new IndexValue(Guid.NewGuid(), index.Id, endPeriod, 9_440m);
        context.IndexValues.Add(indexValue);

        var original = ConfirmAdjustment(
            contract, index.Id, basePeriod, 8_000m, endPeriod, 9_440m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, confirmingUser.Id);

        await context.SaveChangesAsync();

        // The correction: IndexValue.Correct mutates the published level in place (spec
        // "A Correction Produces a New Adjustment, Never a Rewrite") — the confirmed
        // RentAdjustment above must remain exactly as it was.
        indexValue.Correct(9_400m);

        var correction = ConfirmAdjustment(
            contract, index.Id, basePeriod, 8_000m, endPeriod, 9_400m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, confirmingUser.Id,
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

        // users-and-roles (task 3.10b): `uploaded_by` (text) no longer exists on
        // contract_documents — the AddUsersAndRoles migration drops it in favour of the
        // NOT NULL FK `uploaded_by_user_id`. A real AppUser row is seeded first so the raw
        // SQL insert below satisfies that FK; this test still proves only the UNRELATED
        // `kind` CHECK constraint, not anything about the FK itself.
        var uploader = new AppUser(Guid.NewGuid(), $"kindtester-{Guid.NewGuid():N}"[..24], "Kind Check Tester");
        context.AppUsers.Add(uploader);

        var contract = NewContractWithUnit(context);
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO contract_documents
                    (id, contract_id, storage_path, file_name, content_type, uploaded_at, uploaded_by_user_id, kind)
                VALUES
                    ({Guid.NewGuid()}, {contract.Id}, 'path', 'file.pdf', 'application/pdf',
                     {DateTimeOffset.UtcNow}, {uploader.Id}, 'Draft')
                """));
    }

    [SkippableFact]
    public async Task UploadedByUserId_ResolvesToAppUserRow_AndPlainUploadedByColumnIsGone()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        // Spec test 30 (delta half): the "plain identifier" requirement is gone from the
        // living specification, and this asserts its physical trace — the `uploaded_by`
        // text column — is gone from the schema too.
        await using (var columnCheck = connection.CreateCommand())
        {
            columnCheck.CommandText =
                """
                SELECT column_name FROM information_schema.columns
                WHERE table_name = 'contract_documents' AND column_name = 'uploaded_by'
                """;
            await using var reader = await columnCheck.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync(), "uploaded_by must no longer exist on contract_documents.");
        }

        var uploader = new AppUser(
            Guid.NewGuid(), $"uploader-{Guid.NewGuid():N}"[..20], "Uploader Tester");
        context.AppUsers.Add(uploader);

        var contract = NewContractWithUnit(context);
        context.Contracts.Add(contract);

        var document = new ContractDocument(
            Guid.NewGuid(), contract.Id, "leases/original.pdf", "original.pdf",
            "application/pdf", DateTimeOffset.UtcNow, uploader.Id, DocumentKind.Original);
        context.ContractDocuments.Add(document);

        await context.SaveChangesAsync();

        await using var readContext = _fixture.CreateDbContext();
        var reloaded = await readContext.ContractDocuments.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(uploader.Id, reloaded.UploadedByUserId);

        var resolvedUploader = await readContext.AppUsers.SingleAsync(u => u.Id == reloaded.UploadedByUserId);
        Assert.Equal(uploader.Username, resolvedUploader.Username);
    }

    [SkippableFact]
    public async Task ConfirmedByColumn_IsNullable()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        // Spec test 32: the migration adds `confirmed_by` nullable and issues no `UPDATE`
        // against any existing row (proven by the migration's own source — task 3.3 — since
        // no pre-existing rows exist in this fresh-per-test container to backfill in the
        // first place). What is observable here is the resulting column shape: nullable,
        // with no NOT NULL constraint, so a row with no confirmer stays representable.
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT is_nullable FROM information_schema.columns
            WHERE table_name = 'rent_adjustments' AND column_name = 'confirmed_by'
            """;

        var isNullable = (string?)await command.ExecuteScalarAsync();
        Assert.Equal("YES", isNullable);
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
        var uploader = SeedAppUser(context, "docuploader");

        context.ContractDocuments.AddRange(
            new ContractDocument(
                Guid.NewGuid(), contract.Id, "leases/original.pdf", "original.pdf",
                "application/pdf", DateTimeOffset.UtcNow, uploader.Id, DocumentKind.Original),
            new ContractDocument(
                Guid.NewGuid(), contract.Id, "leases/addendum-1.pdf", "addendum-1.pdf",
                "application/pdf", DateTimeOffset.UtcNow, uploader.Id, DocumentKind.Addendum));

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

    [SkippableFact]
    public async Task SameIndexAndPeriodTwice_RejectedByUniqueIndex()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var index = new EconomicIndex(Guid.NewGuid(), $"IPC-{Guid.NewGuid():N}");
        context.EconomicIndices.Add(index);

        // One published value per index per period. Two rows for the same month would make the
        // variation for any interval spanning it ambiguous, and nothing downstream could tell
        // which one it should have used.
        context.IndexValues.AddRange(
            new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 8), 8_000m),
            new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 8), 9_440m));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task DuplicateActiveIndexName_RejectedByPartialUniqueIndex()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var name = $"IPC-{Guid.NewGuid():N}";

        context.EconomicIndices.AddRange(
            new EconomicIndex(Guid.NewGuid(), name),
            new EconomicIndex(Guid.NewGuid(), name));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// Spec test 34 (users-and-roles task 4.24): re-runs the archived append-only assertions
    /// above (<see cref="AppendOnlyTrigger_RejectsRawUpdateAndDelete"/>), but this time
    /// connected as each real application role in turn, not the fixture's superuser — proving
    /// the trigger still rejects both roles after this change adds INSERT to
    /// <c>rent_adjustments</c> for both of them.
    /// </summary>
    [SkippableFact]
    public async Task AppendOnlyTrigger_StillRejectsUpdateAndDelete_ForBothApplicationRoles()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var (contract, index) = SeedContractWithClause(context);
        var confirmingUser = SeedAppUser(context);
        var adjustment = ConfirmAdjustment(
            contract, index.Id,
            new IndexPeriod(2026, 1), 8_000m,
            new IndexPeriod(2026, 7), 9_440m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, confirmingUser.Id);

        await context.SaveChangesAsync();

        await AccessTestSupport.ProvisionUserAsync(context, "trigger34-empleado", UserRole.Empleado);
        await AccessTestSupport.ProvisionUserAsync(context, "trigger34-admin", UserRole.Admin);

        foreach (var rawUsername in new[] { "trigger34-empleado", "trigger34-admin" })
        {
            await using var connection = AccessTestSupport.BuildRawConnectionAs(context, rawUsername);
            await connection.OpenAsync();

            await using (var updateCommand = connection.CreateCommand())
            {
                updateCommand.CommandText =
                    $"UPDATE rent_adjustments SET coefficient = 99 WHERE id = '{adjustment.Id}'";
                await Assert.ThrowsAsync<PostgresException>(() => updateCommand.ExecuteNonQueryAsync());
            }

            await using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.CommandText = $"DELETE FROM rent_adjustments WHERE id = '{adjustment.Id}'";
                await Assert.ThrowsAsync<PostgresException>(() => deleteCommand.ExecuteNonQueryAsync());
            }
        }
    }

    [SkippableFact]
    public async Task DiscontinuedIndexFreesItsNameForASuccessor()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        var name = $"IPC-{Guid.NewGuid():N}";

        await using (var writeContext = _fixture.CreateDbContext())
        {
            var retired = new EconomicIndex(Guid.NewGuid(), name);
            retired.MarkDiscontinued(new IndexPeriod(2026, 3));
            writeContext.EconomicIndices.Add(retired);
            await writeContext.SaveChangesAsync();
        }

        // The unique index is partial — scoped to `discontinued_from IS NULL` — so a rebased
        // index may reuse the retired one's name. This is the half of the constraint that makes
        // the supersession chain usable at all; a plain unique index would forbid it.
        await using var context = _fixture.CreateDbContext();
        context.EconomicIndices.Add(new EconomicIndex(Guid.NewGuid(), name));

        await context.SaveChangesAsync();

        var carryingTheName = await context.EconomicIndices
            .Where(i => i.Name == name)
            .ToListAsync();

        Assert.Equal(2, carryingTheName.Count);
        Assert.Single(carryingTheName, i => i.DiscontinuedFrom is null);
    }
}
