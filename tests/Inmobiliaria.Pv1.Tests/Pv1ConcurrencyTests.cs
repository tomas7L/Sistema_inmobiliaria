using Inmobiliaria.Infrastructure.Tests;

// The three tests share one database and reset it between them: they must never overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Inmobiliaria.Pv1.Tests;

/// <summary>
/// Spec Tests 17, 31 and 70, the very same bodies that run against Testcontainers
/// (<see cref="CollectionDeskConcurrencyScenarios"/>), here against the database named by
/// INMOBILIARIA_PV1_DB. Each starts from a reset database; Test 17 and 31 assert database-wide totals.
/// </summary>
[Collection(Pv1Collection.Name)]
public sealed class Pv1ConcurrencyTests
{
    private readonly Pv1Fixture _fixture;

    public Pv1ConcurrencyTests(Pv1Fixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Test17_TwoSessions_PayingTheSamePeriod_OneSucceeds_TheOtherIsRefusedAndWritesNothing()
    {
        Skip.IfNot(_fixture.IsConfigured, _fixture.SkipReason);

        await CollectionDeskConcurrencyScenarios
            .TwoSessions_PayingTheSamePeriod_OneSucceeds_TheOtherIsRefusedAndWritesNothing(FreshWorldAsync);
    }

    [SkippableFact]
    public async Task Test31_SeveralSessions_Issuing_AtOnce_FormOneUnbrokenRun_WithEachPairAdjacent()
    {
        Skip.IfNot(_fixture.IsConfigured, _fixture.SkipReason);

        await CollectionDeskConcurrencyScenarios
            .SeveralSessions_Issuing_AtOnce_FormOneUnbrokenRun_WithEachPairAdjacent(FreshWorldAsync);
    }

    [SkippableFact]
    public async Task Test70_AnEarlyPayment_RacingTheLazyAccrual_LeavesOneAccrual_AndTheLoserWritesNothing()
    {
        Skip.IfNot(_fixture.IsConfigured, _fixture.SkipReason);

        await CollectionDeskConcurrencyScenarios
            .AnEarlyPayment_RacingTheLazyAccrual_LeavesOneAccrual_AndTheLoserWritesNothing(FreshWorldAsync);
    }

    private async Task<DeskWorld> FreshWorldAsync()
    {
        await _fixture.ResetAsync();
        return DeskWorld.Over(_fixture.ConnectionString!);
    }
}
