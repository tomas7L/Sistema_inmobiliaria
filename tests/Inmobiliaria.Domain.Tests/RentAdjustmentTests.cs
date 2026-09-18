using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// Covers the invariant from task 2.7: `(Kind == Correction) == (CorrectsAdjustmentId != null)`.
/// No numbered spec test names this directly — it is the guard that makes spec test 14 (a
/// correction always names the adjustment it corrects) representable at all.
/// </summary>
public class RentAdjustmentTests
{
    private static AdjustmentProposal MinimalProposal()
    {
        var indexId = Guid.NewGuid();
        var indexValue = new RentAdjustmentIndexValue(
            indexId, indexId, new IndexPeriod(2026, 1), 100m, new IndexPeriod(2026, 6), 110m, 10m);

        return new AdjustmentProposal(100_000m, [indexValue], CombinationRule.Single, 10m, new DateOnly(2026, 7, 1));
    }

    [Fact]
    public void Confirm_CorrectionKindWithoutCorrectsId_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => RentAdjustment.Confirm(
            Guid.NewGuid(), Guid.NewGuid(), MinimalProposal(), DateTimeOffset.UtcNow, AdjustmentKind.Correction));
    }

    [Fact]
    public void Confirm_RegularKindWithCorrectsId_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => RentAdjustment.Confirm(
            Guid.NewGuid(), Guid.NewGuid(), MinimalProposal(), DateTimeOffset.UtcNow,
            AdjustmentKind.Regular, correctsAdjustmentId: Guid.NewGuid()));
    }

    [Fact]
    public void Confirm_CorrectionKindWithCorrectsId_IsAccepted()
    {
        var adjustment = RentAdjustment.Confirm(
            Guid.NewGuid(), Guid.NewGuid(), MinimalProposal(), DateTimeOffset.UtcNow,
            AdjustmentKind.Correction, correctsAdjustmentId: Guid.NewGuid());

        Assert.Equal(AdjustmentKind.Correction, adjustment.Kind);
    }
}
