using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

public class AdjustmentMathTests
{
    private static readonly IndexPeriod BasePeriod = new(2026, 1);
    private static readonly IndexPeriod EndPeriod = new(2026, 6);

    [Fact]
    public void Compute_AveragesVariations_NotRawLevels()
    {
        // Spec test 1: IPC 8,000 -> 9,440 (+18%), RIPTE 1,200,000 -> 1,344,000 (+12%).
        // Averaging the raw levels instead of the variations gives a wildly different,
        // meaningless number, so this worked example genuinely discriminates a buggy
        // level-averaging implementation from a correct variation-averaging one.
        var ipcId = Guid.NewGuid();
        var ripteId = Guid.NewGuid();

        var inputs = new[]
        {
            new IndexVariationInput(ipcId, "IPC", ipcId, BasePeriod, 8_000m, ipcId, EndPeriod, 9_440m),
            new IndexVariationInput(ripteId, "RIPTE", ripteId, BasePeriod, 1_200_000m, ripteId, EndPeriod, 1_344_000m),
        };

        var result = AdjustmentMath.Compute(CombinationRule.Average, inputs);

        Assert.Equal(15m, result.Coefficient);

        var proposal = new AdjustmentProposal(450_000m, result.IndexValues, CombinationRule.Average, result.Coefficient!.Value, new DateOnly(2026, 7, 1));
        Assert.Equal(517_500m, proposal.TruncatedNewCanon);
    }

    [Fact]
    public void Compute_SingleIndexClause_SkipsAveraging()
    {
        // Spec test 2 / scenario "Single-index clause needs no averaging".
        var iclId = Guid.NewGuid();
        var inputs = new[]
        {
            new IndexVariationInput(iclId, "ICL", iclId, BasePeriod, 100m, iclId, EndPeriod, 110m),
        };

        var result = AdjustmentMath.Compute(CombinationRule.Single, inputs);

        Assert.Equal(10m, result.Coefficient);
    }

    [Fact]
    public void Compute_OneOfTwoIndicesMissing_CoefficientStaysNull()
    {
        // Spec test 8: a partial index set is never averaged.
        var ipcId = Guid.NewGuid();
        var ripteId = Guid.NewGuid();

        var inputs = new[]
        {
            new IndexVariationInput(ipcId, "IPC", ipcId, BasePeriod, 8_000m, ipcId, EndPeriod, 9_440m),
            new IndexVariationInput(ripteId, "RIPTE", null, BasePeriod, null, null, EndPeriod, null),
        };

        var result = AdjustmentMath.Compute(CombinationRule.Average, inputs);

        Assert.Null(result.Coefficient);
        Assert.NotNull(result.PendingReason);
    }

    [Fact]
    public void Compute_BaseAndEndResolveToDifferentIndices_IsRefused()
    {
        // Decision 7's splice rule (extra test, not a numbered spec test): a rebase mid-period
        // means two different series, and the ratio across them must be refused, not computed.
        var indexA = Guid.NewGuid();
        var indexB = Guid.NewGuid();

        var inputs = new[]
        {
            new IndexVariationInput(indexA, "IPC", indexA, BasePeriod, 8_000m, indexB, EndPeriod, 500m),
        };

        var result = AdjustmentMath.Compute(CombinationRule.Single, inputs);

        Assert.Null(result.Coefficient);
        Assert.Empty(result.IndexValues);
    }
}
