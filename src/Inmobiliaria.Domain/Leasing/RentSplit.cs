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

    /// <summary>
    /// Converts an amount typed per unit into percentages (gestion-de-alquileres spec Requirement 7). Each is truncated to
    /// 6 decimal places, and the residue goes to the largest resulting share, ties to the smallest <see cref="Guid"/>.
    /// Null when an amount is not positive or they do not total <paramref name="canon"/>: there is no split to convert.
    /// </summary>
    public static IReadOnlyList<UnitShare>? FromAmounts(decimal canon, IReadOnlyList<(Guid UnitId, decimal Amount)> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        if (amounts.Count == 0 || amounts.Any(a => a.Amount <= 0m))
        {
            return null;
        }

        // Taken from what is left of the canon, since amounts an operator typed may not even fit in a sum.
        var left = canon;
        foreach (var (_, amount) in amounts)
        {
            if (amount > left)
            {
                return null;
            }

            left -= amount;
        }

        if (left != 0m)
        {
            return null;
        }

        var shares = amounts
            .Select(a => new UnitShare(a.UnitId, Math.Floor(a.Amount / canon * HundredPercent * SharePrecisionScale) / SharePrecisionScale))
            .ToList();
        var residue = HundredPercent - shares.Sum(s => s.Percentage);
        var winner = shares.OrderByDescending(s => s.Percentage).ThenBy(s => s.UnitId).First();

        return shares.Select(s => s.UnitId == winner.UnitId ? s with { Percentage = s.Percentage + residue } : s).ToList();
    }
}
