namespace Inmobiliaria.Domain.Indices;

/// <summary>
/// A published level for one <see cref="EconomicIndex"/> in one <see cref="IndexPeriod"/>.
/// Stores the number the publisher actually published (design Decision 1) — never a derived
/// variation. RIPTE is denominated in pesos but is still a level here, never money: the
/// whole-peso truncation rule does not apply to it.
/// </summary>
public sealed class IndexValue
{
    public Guid Id { get; }
    public Guid EconomicIndexId { get; }
    public IndexPeriod Period { get; }
    public decimal Level { get; private set; }

    public IndexValue(Guid id, Guid economicIndexId, IndexPeriod period, decimal level)
    {
        ValidateLevel(level);

        Id = id;
        EconomicIndexId = economicIndexId;
        Period = period;
        Level = level;
    }

    /// <summary>
    /// Corrects the published level in place. The row's <see cref="Id"/> stays stable so a
    /// confirmed adjustment that already used this value keeps its own snapshotted numbers
    /// unaffected — a correction produces a new adjustment, never a rewrite of the confirmed
    /// one (spec "A Correction Produces a New Adjustment, Never a Rewrite").
    /// </summary>
    public void Correct(decimal newLevel)
    {
        ValidateLevel(newLevel);
        Level = newLevel;
    }

    private static void ValidateLevel(decimal level)
    {
        if (level <= 0m)
        {
            throw new ArgumentException("An index level must be greater than zero.", nameof(level));
        }
    }
}
