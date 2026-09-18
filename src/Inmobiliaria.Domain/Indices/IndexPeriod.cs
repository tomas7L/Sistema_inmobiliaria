namespace Inmobiliaria.Domain.Indices;

/// <summary>
/// A first-class calendar period identified by year and month (design Decision 2). The
/// domain never carries a day component: rent is charged per whole month, and wherever this
/// reaches storage it maps to the first day of the month.
/// </summary>
public readonly record struct IndexPeriod : IComparable<IndexPeriod>
{
    public int Year { get; }
    public int Month { get; }

    public IndexPeriod(int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        }

        Year = year;
        Month = month;
    }

    /// <summary>Adds <paramref name="months"/> whole months, rolling the year as needed.</summary>
    public IndexPeriod AddMonths(int months)
    {
        var totalMonths = (Year * 12) + (Month - 1) + months;
        var year = (int)Math.Floor(totalMonths / 12.0);
        var month = totalMonths - (year * 12) + 1;

        return new IndexPeriod(year, month);
    }

    public int CompareTo(IndexPeriod other)
    {
        var yearComparison = Year.CompareTo(other.Year);

        return yearComparison != 0 ? yearComparison : Month.CompareTo(other.Month);
    }

    public static bool operator <(IndexPeriod left, IndexPeriod right) => left.CompareTo(right) < 0;

    public static bool operator >(IndexPeriod left, IndexPeriod right) => left.CompareTo(right) > 0;

    public static bool operator <=(IndexPeriod left, IndexPeriod right) => left.CompareTo(right) <= 0;

    public static bool operator >=(IndexPeriod left, IndexPeriod right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
