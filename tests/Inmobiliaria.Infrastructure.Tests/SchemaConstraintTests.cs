using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
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

    private static Contract NewContract(decimal monthlyRent = 100_000m) =>
        new(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), monthlyRent);

    [SkippableFact]
    public async Task DeferredTrigger_AllowsValidTwoUnitSplitInsertedRowByRow()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();

        var unitA = new PropertyUnit(Guid.NewGuid(), TestAddress("100"));
        var unitB = new PropertyUnit(Guid.NewGuid(), TestAddress("200"));
        context.Units.AddRange(unitA, unitB);

        var contract = NewContract();
        contract.SetUnitShares([new UnitShare(unitA.Id, 60m), new UnitShare(unitB.Id, 40m)]);
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

        var unit = new PropertyUnit(Guid.NewGuid(), TestAddress("300"));
        context.Units.Add(unit);
        var contract = NewContract();
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();

        // Raw insert deliberately bypasses Contract.SetUnitShares: this proves the DATABASE
        // trigger — not the domain invariant — rejects a split that never reaches 100%.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO contract_units (contract_id, unit_id, share_percentage)
            VALUES ({contract.Id}, {unit.Id}, 60)
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

        var contract = NewContract();
        contract.SetUnitShares([new UnitShare(unit.Id, 100m)]);
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
        var contract = NewContract();
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
        var contract = NewContract();
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

        var contract = NewContract();
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

        var contract = NewContract();
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
            Guid.NewGuid(), new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31), 80_000m);
        expiredContract.SetUnitShares([new UnitShare(unit.Id, 100m)]);

        var currentContract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), 120_000m);
        currentContract.SetUnitShares([new UnitShare(unit.Id, 100m)]);

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

        var contract = NewContract();
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
