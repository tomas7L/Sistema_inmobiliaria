namespace Inmobiliaria.Domain.Indices;

/// <summary>
/// What a typed index level must satisfy (gestion-de-alquileres spec Requirement 12, design D23). A level far from its
/// neighbour is a warning the operator can confirm, since an index can really jump; everything else is a refusal.
/// </summary>
public static class IndexEntryRules
{
    /// <summary>Beyond this share of the earlier level per month elapsed, a level warns (design D23).</summary>
    public const decimal FarLevelPerMonth = 0.25m;

    private const int StoredDecimals = 6;

    /// <summary><c>numeric(18,6)</c> keeps twelve whole digits.</summary>
    private const decimal FirstLevelTooLarge = 1_000_000_000_000m;

    /// <summary>The first year a period can be (spec Decision 53): before it, a year is a typing error such as "201".</summary>
    public const int FirstYear = 2000;

    /// <summary>
    /// Whether <paramref name="period"/> can exist for <paramref name="indexName"/> (spec Decision 53): its year between
    /// <see cref="FirstYear"/> and the current one, and never a month that has not begun. ICL is published daily, so its
    /// current month is allowed; every other index is published after its month, so its current month is not out yet.
    /// </summary>
    public static IndexRefusal? CheckPeriod(IndexPeriod period, DateOnly today, string indexName)
    {
        ArgumentNullException.ThrowIfNull(indexName);

        var current = new IndexPeriod(today.Year, today.Month);

        if (period.Year < FirstYear || period.Year > today.Year)
        {
            return IndexRefusal.YearOutOfRange;
        }

        if (period > current)
        {
            return IndexRefusal.FuturePeriod;
        }

        return period == current && !IsPublishedDaily(indexName) ? IndexRefusal.NotPublishedYet : null;
    }

    /// <summary>ICL, published every day by the BCRA, so the value of the 1st exists on the 1st.</summary>
    public static bool IsPublishedDaily(string indexName) => indexName == "ICL";

    public static IndexRefusal? CheckLevel(decimal level)
    {
        if (level <= 0m)
        {
            return IndexRefusal.LevelNotPositive;
        }

        if (level >= FirstLevelTooLarge)
        {
            return IndexRefusal.LevelTooLarge;
        }

        return decimal.Round(level, StoredDecimals) != level ? IndexRefusal.TooManyDecimals : null;
    }

    /// <summary>The latest value before <paramref name="period"/>, which is what a level is compared with.</summary>
    public static IndexValueListing? NearestEarlier(IndexPeriod period, IEnumerable<IndexValueListing> values) =>
        values.Where(value => value.Period < period).MaxBy(value => value.Period);

    /// <param name="level">The level typed.</param>
    /// <param name="period">The month it is for.</param>
    /// <param name="nearestEarlier">From <see cref="NearestEarlier"/>. With none, nothing can be far.</param>
    public static bool IsFar(decimal level, IndexPeriod period, IndexValueListing? nearestEarlier)
    {
        if (nearestEarlier is null)
        {
            return false;
        }

        var months = ((period.Year - nearestEarlier.Period.Year) * 12) + period.Month - nearestEarlier.Period.Month;

        return Math.Abs(level - nearestEarlier.Level) > nearestEarlier.Level * FarLevelPerMonth * months;
    }
}
