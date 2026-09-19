using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// One referenced index's contribution, already resolved by the caller through
/// <see cref="IndexResolver"/> and looked up against stored <see cref="IndexValue"/> rows.
/// A null resolved id or level means "not available for this period" — this type carries no
/// dependency on catalog or repository lookups; it is pure input.
/// </summary>
public sealed record IndexVariationInput(
    Guid ReferencedIndexId,
    string IndexName,
    Guid? BaseResolvedIndexId,
    IndexPeriod BasePeriod,
    decimal? BaseLevel,
    Guid? EndResolvedIndexId,
    IndexPeriod EndPeriod,
    decimal? EndLevel);

/// <summary>
/// Outcome of <see cref="AdjustmentMath.Compute"/>: either every referenced index resolved and
/// a <see cref="Coefficient"/> exists, or it does not and the adjustment stays pending.
/// <see cref="Coefficient"/> is null exactly when it could not be computed from every
/// referenced index — never from a partial set (spec "A Partial Index Set Is Never Averaged").
/// </summary>
public sealed record AdjustmentMathResult(
    decimal? Coefficient,
    IReadOnlyList<RentAdjustmentIndexValue> IndexValues,
    string? PendingReason);

/// <summary>
/// Computes the adjustment coefficient as the average of each referenced index's PERCENTAGE
/// VARIATION over the period — never of raw levels (spec "Coefficient Is the Average of
/// Percentage Variations, Never of Raw Levels"; design Decision 4). IPC is a dimensionless
/// index number and RIPTE is an amount in pesos: averaging their levels directly would produce
/// a value with no economic meaning without ever raising an error, which is exactly the trap
/// this type exists to avoid.
/// </summary>
public static class AdjustmentMath
{
    public static AdjustmentMathResult Compute(CombinationRule combination, IReadOnlyList<IndexVariationInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Count == 0)
        {
            throw new ArgumentException("At least one index input is required.", nameof(inputs));
        }

        var resolved = new List<RentAdjustmentIndexValue>(inputs.Count);

        foreach (var input in inputs)
        {
            if (input.BaseResolvedIndexId is not { } baseIndexId || input.EndResolvedIndexId is not { } endIndexId)
            {
                return Pending($"'{input.IndexName}' has no index resolved for its period.");
            }

            // Design Decision 7's splice rule: the base and end periods must resolve to the
            // SAME index. A rebase mid-period means two different series, and a ratio across
            // them is meaningless, not merely imprecise — refused rather than computed.
            if (baseIndexId != endIndexId)
            {
                return Pending(
                    $"'{input.IndexName}' resolves to different indices across the period; the splice is refused.");
            }

            if (input.BaseLevel is not { } baseLevel || input.EndLevel is not { } endLevel)
            {
                return Pending($"'{input.IndexName}' has no published value for the required period.");
            }

            // Rounded ONCE here, to the column's precision (design Decision 4) — never
            // progressively re-rounded afterward.
            var variation = decimal.Round(((endLevel / baseLevel) - 1m) * 100m, 6);

            resolved.Add(new RentAdjustmentIndexValue(
                input.ReferencedIndexId, baseIndexId, input.BasePeriod, baseLevel, input.EndPeriod, endLevel, variation));
        }

        // `Single` skips the averaging step entirely, per spec; `Average` is the mean of the
        // already-rounded variations, rounded again to fit the coefficient's own precision.
        var coefficient = combination == CombinationRule.Single
            ? resolved[0].Variation
            : decimal.Round(resolved.Average(r => r.Variation), 6);

        return new AdjustmentMathResult(coefficient, resolved, PendingReason: null);
    }

    private static AdjustmentMathResult Pending(string reason) => new(null, [], reason);
}
