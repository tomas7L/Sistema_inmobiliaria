namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Computes an equal rent split across units, with a deterministic residue rule when
/// 100% does not divide evenly. `decimal` throughout — never floating point.
/// </summary>
public static class RentSplit
{
    private const decimal HundredPercent = 100m;

    /// <summary>Matches the `numeric(9,6)` column precision: 6 decimal places.</summary>
    private const decimal SharePrecisionScale = 1_000_000m;

    /// <summary>
    /// Splits 100% equally across <paramref name="unitIds"/>. Each share is truncated to
    /// 6 decimal places; the leftover residue is added to the largest resulting share,
    /// ties broken by the smallest <see cref="Guid"/> (in an equal split every share
    /// ties, so the lowest-id unit always absorbs the residue).
    /// </summary>
    public static IReadOnlyList<UnitShare> Equal(IReadOnlyList<Guid> unitIds)
    {
        ArgumentNullException.ThrowIfNull(unitIds);

        if (unitIds.Count == 0)
        {
            throw new ArgumentException("At least one unit is required to compute a split.", nameof(unitIds));
        }

        var count = unitIds.Count;
        var baseShare = Math.Floor(HundredPercent / count * SharePrecisionScale) / SharePrecisionScale;
        var shares = new decimal[count];

        for (var i = 0; i < count; i++)
        {
            shares[i] = baseShare;
        }

        var residue = HundredPercent - (baseShare * count);

        if (residue > 0m)
        {
            var winnerIndex = 0;

            for (var i = 1; i < count; i++)
            {
                if (unitIds[i].CompareTo(unitIds[winnerIndex]) < 0)
                {
                    winnerIndex = i;
                }
            }

            shares[winnerIndex] += residue;
        }

        var result = new UnitShare[count];

        for (var i = 0; i < count; i++)
        {
            result[i] = new UnitShare(unitIds[i], shares[i]);
        }

        return result;
    }
}
