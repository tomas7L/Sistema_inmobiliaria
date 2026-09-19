using Inmobiliaria.Domain.Indices;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared value converters mapping the domain's <see cref="IndexPeriod"/> onto a first-of-month
/// <c>date</c> column (design.md Decision 2). The converter only shapes the value; the
/// <c>EXTRACT(DAY FROM ...) = 1</c> CHECK declared alongside each column that uses it is what
/// actually enforces the day-1 rule, so the requirement cannot be violated by arithmetic.
/// </summary>
internal static class IndexPeriodValueConverter
{
    public static readonly ValueConverter<IndexPeriod, DateOnly> NonNullable = new(
        period => new DateOnly(period.Year, period.Month, 1),
        date => new IndexPeriod(date.Year, date.Month));

    public static readonly ValueConverter<IndexPeriod?, DateOnly?> Nullable = new(
        period => period.HasValue ? new DateOnly(period.Value.Year, period.Value.Month, 1) : null,
        date => date.HasValue ? new IndexPeriod(date.Value.Year, date.Value.Month) : null);
}
