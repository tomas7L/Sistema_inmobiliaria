using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// M6 <c>AddAdjustmentAppliesFrom</c> (gestion-de-alquileres design D1, D8): the database refuses a second Regular adjustment
/// for one due date, a second correction of one adjustment, and an applied-from period that is not a first of month, whatever
/// wrote them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdjustmentAppliesFromSchemaTests
{
    private static readonly DateOnly July = new(2026, 7, 1);

    private readonly PostgresFixture _fixture;

    public AdjustmentAppliesFromSchemaTests(PostgresFixture fixture) => _fixture = fixture;

    private static async Task<(Guid Contract, Guid Adjustment)> SeedConfirmedAsync(InmobiliariaDbContext context)
    {
        var user = await AccessTestSupport.ProvisionUserAsync(context, $"m6-{Guid.NewGuid():N}"[..20], UserRole.Admin);
        var (contract, index) = SchemaConstraintTests.SeedContractWithClause(context);
        var adjustment = SchemaConstraintTests.ConfirmAdjustment(
            contract, index.Id, new IndexPeriod(2026, 1), 100m, new IndexPeriod(2026, 6), 110m, July, DateTimeOffset.UtcNow, user);
        await context.SaveChangesAsync();
        return (contract.Id, adjustment.Id);
    }

    private static Task InsertAsync(
        InmobiliariaDbContext context, Guid contract, DateOnly effectiveDate, string kind, Guid? corrects = null, DateOnly? appliesFrom = null) =>
        context.Database.ExecuteSqlAsync($"""
            INSERT INTO rent_adjustments (id, contract_id, effective_date, previous_canon, new_canon, coefficient, kind, corrects_adjustment_id, confirmed_at, applies_from)
            VALUES ({Guid.NewGuid()}, {contract}, {effectiveDate}, 100000, 110000, 10, {kind}, {corrects}, now(), {appliesFrom})
            """);

    [SkippableFact]
    public async Task ASecondRegularAdjustment_ForOneDueDate_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, _) = await SeedConfirmedAsync(context);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, contract, July, "Regular"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refused.SqlState);
        Assert.Equal("ux_rent_adjustments_one_regular_per_due_date", refused.ConstraintName);
    }

    [SkippableFact]
    public async Task ACorrection_ReusesItsDueDate_ButASecondCorrectionOfTheSameAdjustment_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, adjustment) = await SeedConfirmedAsync(context);

        await InsertAsync(context, contract, July, "Correction", adjustment);
        var refused = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(context, contract, July, "Correction", adjustment));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refused.SqlState);
        Assert.Equal("ux_rent_adjustments_one_correction_per_adjustment", refused.ConstraintName);
    }

    [SkippableFact]
    public async Task AnotherDueDate_OnTheSameContract_IsAccepted()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, _) = await SeedConfirmedAsync(context);

        await InsertAsync(context, contract, new DateOnly(2027, 1, 1), "Regular", appliesFrom: new DateOnly(2027, 2, 1));

        Assert.Equal(2, await context.RentAdjustments.CountAsync(a => a.ContractId == contract));
    }

    [SkippableFact]
    public async Task AnAppliedFromPeriodThatIsNotAFirstOfMonth_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, _) = await SeedConfirmedAsync(context);

        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(context, contract, new DateOnly(2027, 1, 1), "Regular", appliesFrom: new DateOnly(2027, 2, 10)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal("ck_rent_adjustments_applies_from_first_of_month", refused.ConstraintName);
    }

    [SkippableFact]
    public async Task ARowWithoutAppliesFrom_IsStillAccepted()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var context = _fixture.CreateDbContext();
        var (contract, _) = await SeedConfirmedAsync(context);

        Assert.Null((await context.RentAdjustments.AsNoTracking().SingleAsync(a => a.ContractId == contract)).AppliesFrom);
    }
}
