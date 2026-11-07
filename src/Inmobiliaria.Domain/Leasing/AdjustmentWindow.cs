using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// The two months an adjustment reads (gestion-de-alquileres design D3): for due date D, interval N and offset K, from D − K − N
/// to D − K. Consecutive adjustments of a clause share a month, the end of one being the base of the next, so none is skipped or
/// counted twice.
/// </summary>
public readonly record struct AdjustmentWindow(IndexPeriod Base, IndexPeriod End)
{
    public static AdjustmentWindow For(DateOnly dueDate, int intervalMonths, int windowOffsetMonths)
    {
        var end = new IndexPeriod(dueDate.Year, dueDate.Month).AddMonths(-windowOffsetMonths);

        return new AdjustmentWindow(end.AddMonths(-intervalMonths), end);
    }
}
