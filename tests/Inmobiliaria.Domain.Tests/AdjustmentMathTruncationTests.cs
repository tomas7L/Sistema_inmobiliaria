using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// Exercises the single truncation site end to end, through <see cref="AdjustmentProposal"/>'s
/// preview and <see cref="RentAdjustment.Confirm"/>'s actual assignment. Coefficient is fixed at
/// 0% in every case, so the previous canon passes through untouched except for the truncation
/// itself — isolating exactly the arithmetic these tests exist to prove.
/// </summary>
public class AdjustmentMathTruncationTests
{
    private static readonly RentAdjustmentIndexValue ZeroVariationIndexValue = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        new IndexPeriod(2026, 1),
        baseLevel: 100m,
        new IndexPeriod(2026, 6),
        endLevel: 100m,
        variation: 0m);

    private static AdjustmentProposal ProposalFor(decimal computedCanon) => new(
        computedCanon, [ZeroVariationIndexValue], CombinationRule.Single, coefficient: 0m, new DateOnly(2026, 7, 1));

    [Fact]
    public void Confirm_ComputedAmountWithCentavos_TruncatesToWholePeso()
    {
        // Spec test 3: a computed 517,483.73 stores 517,483.
        var proposal = ProposalFor(517_483.73m);

        Assert.Equal(517_483.73m, proposal.UntruncatedNewCanon);

        var adjustment = RentAdjustment.Confirm(Guid.NewGuid(), Guid.NewGuid(), proposal, DateTimeOffset.UtcNow);

        Assert.Equal(517_483m, adjustment.NewCanon);
    }

    [Fact]
    public void Confirm_TruncationNeverRoundsUp()
    {
        // Spec test 4, the exact figure observed on the agency's real owner receipt:
        // a computed 47,860.80 stores 47,860, never 47,861.
        var proposal = ProposalFor(47_860.80m);

        var adjustment = RentAdjustment.Confirm(Guid.NewGuid(), Guid.NewGuid(), proposal, DateTimeOffset.UtcNow);

        Assert.Equal(47_860m, adjustment.NewCanon);
        Assert.NotEqual(47_861m, adjustment.NewCanon);
    }

    [Fact]
    public void Confirm_ComputedAmountAlreadyWhole_IsNeverLiftedToARounderFigure()
    {
        // Spec test 5: a computed 517,483 stays 517,483 and must never become 517,500.
        var proposal = ProposalFor(517_483m);

        var adjustment = RentAdjustment.Confirm(Guid.NewGuid(), Guid.NewGuid(), proposal, DateTimeOffset.UtcNow);

        Assert.Equal(517_483m, adjustment.NewCanon);
        Assert.NotEqual(517_500m, adjustment.NewCanon);
    }

    [Fact]
    public void ConfirmAdjustment_ThroughContract_TruncatesExactlyOnceWithNoProgressiveDrift()
    {
        // Spec test 6, domain half: intermediate arithmetic stays `decimal` throughout the real
        // AdjustmentMath -> AdjustmentProposal -> Contract.ConfirmAdjustment pipeline, and the
        // whole-peso rule is applied exactly once — not progressively at every step.
        var contract = ContractTestFactory.CreateActive();
        var indexId = Guid.NewGuid();
        var inputs = new[]
        {
            new IndexVariationInput(
                indexId, "ICL", indexId, new IndexPeriod(2026, 1), 7m, indexId, new IndexPeriod(2026, 6), 8m),
        };
        var mathResult = AdjustmentMath.Compute(CombinationRule.Single, inputs);
        var proposal = new AdjustmentProposal(
            contract.MonthlyRent, mathResult.IndexValues, CombinationRule.Single,
            mathResult.Coefficient!.Value, new DateOnly(2026, 7, 1));

        Assert.Equal(114_285.714m, proposal.UntruncatedNewCanon);

        contract.ConfirmAdjustment(Guid.NewGuid(), proposal, DateTimeOffset.UtcNow);

        Assert.Equal(114_285m, contract.MonthlyRent);
    }
}
