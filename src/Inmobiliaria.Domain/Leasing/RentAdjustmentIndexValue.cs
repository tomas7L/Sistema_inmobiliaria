using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// One index's contribution to a confirmed <see cref="RentAdjustment"/>, snapshotted BY VALUE
/// at confirmation time (design Decision 3): the periods and levels are copies, not a foreign
/// key to the live <see cref="IndexValue"/> row, so a later correction of the source value
/// cannot change history (spec "A Correction Produces a New Adjustment, Never a Rewrite").
/// <see cref="ReferencedIndexId"/> and <see cref="ResolvedIndexId"/> stay ids for display and
/// traceability only — the numbers themselves are copies, never re-derived.
/// </summary>
public sealed class RentAdjustmentIndexValue
{
    public Guid ReferencedIndexId { get; }
    public Guid ResolvedIndexId { get; }
    public IndexPeriod BasePeriod { get; }
    public decimal BaseLevel { get; }
    public IndexPeriod EndPeriod { get; }
    public decimal EndLevel { get; }
    public decimal Variation { get; }

    public RentAdjustmentIndexValue(
        Guid referencedIndexId,
        Guid resolvedIndexId,
        IndexPeriod basePeriod,
        decimal baseLevel,
        IndexPeriod endPeriod,
        decimal endLevel,
        decimal variation)
    {
        if (baseLevel <= 0m)
        {
            throw new ArgumentException("A snapshotted base level must be greater than zero.", nameof(baseLevel));
        }

        if (endLevel <= 0m)
        {
            throw new ArgumentException("A snapshotted end level must be greater than zero.", nameof(endLevel));
        }

        ReferencedIndexId = referencedIndexId;
        ResolvedIndexId = resolvedIndexId;
        BasePeriod = basePeriod;
        BaseLevel = baseLevel;
        EndPeriod = endPeriod;
        EndLevel = endLevel;
        Variation = variation;
    }
}
