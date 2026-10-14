using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// The materialiser against a contract that predates the system or carries a fee plan (cobranza-y-
/// recibos spec Tests 71 Infrastructure part and 76, task 2.7). It reads the cutover date and the plan
/// from the contract's own columns, so these tests prove the columns are mapped and honoured.
///
/// Dates sit in 2025 on purpose: the worklist query materialises EVERY account in the shared
/// database, and nothing seeded elsewhere starts before 2026.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MaterialiserCutoverTests
{
    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    private readonly PostgresFixture _fixture;

    public MaterialiserCutoverTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static DateOnly Month(int year, int month) => new(year, month, 1);

    private static async Task<(Contract Contract, ContractAccount Account)> SeedAsync(
        InmobiliariaDbContext context,
        DateOnly? cutover = null,
        ContractualFeePlan? plan = null)
    {
        var unit = new PropertyUnit(
            Guid.NewGuid(),
            new Address("Fake Street", Guid.NewGuid().ToString("N")[..6], "Sunchales", "Santa Fe", "2322"));
        context.Units.Add(unit);

        var contract = new Contract(
            Guid.NewGuid(),
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 12, 31),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(unit.Id, 100m)],
            cutoverDate: cutover,
            contractualFeePlan: plan);
        context.Contracts.Add(contract);

        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        context.ContractAccounts.Add(account);

        await context.SaveChangesAsync();

        return (contract, account);
    }

    private static async Task<List<AccountMovement>> ReadAsync(InmobiliariaDbContext context, Guid accountId) =>
        (await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == accountId))
        .Movements.OrderBy(m => m.Period).ThenBy(m => m.Kind).ToList();

    /// <summary>The cutover date and the plan survive a round trip through the mapped columns.</summary>
    [SkippableFact]
    public async Task TheCutoverDateAndThePlan_RoundTripThroughTheDatabase()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contract, _) = await SeedAsync(
            context, cutover: new DateOnly(2025, 2, 10), plan: new ContractualFeePlan(120_000m, 3));

        await using var fresh = _fixture.CreateDbContext();
        var reloaded = await fresh.Contracts.SingleAsync(c => c.Id == contract.Id);

        Assert.Equal(new DateOnly(2025, 2, 10), reloaded.CutoverDate);
        Assert.Equal(new ContractualFeePlan(120_000m, 3), reloaded.ContractualFeePlan);
    }

    [SkippableFact]
    public async Task AContractWithNoPlanAndNoCutover_RoundTripsAsNulls()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (contract, _) = await SeedAsync(context);

        await using var fresh = _fixture.CreateDbContext();
        var reloaded = await fresh.Contracts.SingleAsync(c => c.Id == contract.Id);

        Assert.Null(reloaded.CutoverDate);
        Assert.Null(reloaded.ContractualFeePlan);
    }

    /// <summary>
    /// Spec test 71, Infrastructure part: materialising a loaded pre-existing account writes no rent
    /// or instalment due on or before the cutover date, and writes the later ones. Instalments 1 and 2
    /// belong to January and February and are skipped; instalment 3 rides with March.
    /// </summary>
    [SkippableFact]
    public async Task AccountOfAPreExistingContract_MaterialisesNothingOnOrBeforeTheCutover_AndTheLaterOnes()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(
            context, cutover: new DateOnly(2025, 2, 10), plan: new ContractualFeePlan(120_000m, 3));

        var written = await new AccountMaterialiser(context)
            .MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2025, 6, 15));

        // Rents for March, April, May and June, plus the third instalment on March.
        Assert.Equal(5, written);

        await using var fresh = _fixture.CreateDbContext();
        var movements = await ReadAsync(fresh, account.Id);

        Assert.DoesNotContain(movements, m => m.Period <= Month(2025, 2));
        Assert.Equal(
            [Month(2025, 3), Month(2025, 4), Month(2025, 5), Month(2025, 6)],
            movements.Where(m => m.Kind == MovementKind.RentAccrual).Select(m => m.Period!.Value));
        var instalment = Assert.Single(movements, m => m.Kind == MovementKind.ContractualFeeInstalment);
        Assert.Equal(Month(2025, 3), instalment.Period);
        Assert.Equal(new DateOnly(2025, 3, 10), instalment.OccurredOn);
        Assert.Equal(120_000m, instalment.Amount);
    }

    /// <summary>A period due exactly on the cutover date is not raised either.</summary>
    [SkippableFact]
    public async Task APeriodDueExactlyOnTheCutoverDate_IsNotRaised()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(context, cutover: new DateOnly(2025, 3, 10));

        await new AccountMaterialiser(context)
            .MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2025, 3, 31));

        await using var fresh = _fixture.CreateDbContext();
        Assert.Empty(await ReadAsync(fresh, account.Id));
    }

    /// <summary>The instalment is written beside the rent, once, and a second pass writes nothing.</summary>
    [SkippableFact]
    public async Task TheInstalment_IsWrittenBesideTheRent_AndNeverTwice()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(context, plan: new ContractualFeePlan(120_000m, 3));
        var materialiser = new AccountMaterialiser(context);

        var first = await materialiser.MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2025, 4, 30));
        var second = await materialiser.MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2025, 4, 30));

        // Four rents and instalments 1 to 3 on January to March.
        Assert.Equal(7, first);
        Assert.Equal(0, second);

        await using var fresh = _fixture.CreateDbContext();
        var movements = await ReadAsync(fresh, account.Id);
        Assert.Equal(
            [Month(2025, 1), Month(2025, 2), Month(2025, 3)],
            movements.Where(m => m.Kind == MovementKind.ContractualFeeInstalment).Select(m => m.Period!.Value));
        Assert.All(
            movements.Where(m => m.Kind == MovementKind.ContractualFeeInstalment),
            m => Assert.Equal(m.Period!.Value.AddDays(9), m.OccurredOn));
    }

    /// <summary>
    /// Task 2.7 note, PR 1 finding R3-materialiser-guard-divergence: a due period that already holds a
    /// monthly opening balance is charged, so the materialiser writes no rent for it. Before the guard
    /// moved from HasAccrualFor to IsChargedFor the domain refused the rent mid-loop and the whole
    /// materialisation threw.
    /// </summary>
    [SkippableFact]
    public async Task APeriodHoldingAMonthlyOpeningBalance_TakesNoRentAndDoesNotAbortTheRest()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(context);

        var typed = await context.ContractAccounts.Include(a => a.Movements).SingleAsync(a => a.Id == account.Id);
        typed.Append(AccountMovement.OpeningBalance(
            Guid.NewGuid(), 160_000m, new DateOnly(2025, 3, 20), Month(2025, 2)));
        await context.SaveChangesAsync();

        var written = await new AccountMaterialiser(context)
            .MaterialiseDueAccrualsThroughAsync(account.Id, new DateOnly(2025, 3, 31));

        // January and March; February is carried by the opening balance.
        Assert.Equal(2, written);

        await using var fresh = _fixture.CreateDbContext();
        var movements = await ReadAsync(fresh, account.Id);
        Assert.Equal(
            [Month(2025, 1), Month(2025, 3)],
            movements.Where(m => m.Kind == MovementKind.RentAccrual).Select(m => m.Period!.Value));
        Assert.Equal(160_000m, Assert.Single(movements, m => m.Kind == MovementKind.OpeningBalance).Amount);
    }

    /// <summary>
    /// Spec test 76: a contract stored with a cutover date and no opening balance yet, read by the mora
    /// worklist, gains no rent or instalment for any period due on or before that date. The worklist
    /// needs no change because it reads through the materialiser, which reads the schedule.
    /// </summary>
    [SkippableFact]
    public async Task AContractWithACutoverAndNoOpeningBalanceYet_GainsNothingWhenTheWorklistReadsIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var context = _fixture.CreateDbContext();
        var (_, account) = await SeedAsync(
            context, cutover: new DateOnly(2025, 6, 30), plan: new ContractualFeePlan(120_000m, 6));

        // Read after the cutover but before July's tenth: without the floor six months of rent and
        // three instalments would be written onto an append-only ledger.
        var worklist = await new MoraWorklistQuery(context, new AccountMaterialiser(context))
            .GetAccountsInMoraAsync(new DateOnly(2025, 7, 5), LeaseTerms);

        Assert.DoesNotContain(worklist, row => row.AccountId == account.Id);

        await using var fresh = _fixture.CreateDbContext();
        Assert.Empty(await ReadAsync(fresh, account.Id));
    }
}
