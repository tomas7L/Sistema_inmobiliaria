namespace Inmobiliaria.Domain.Indices;

/// <summary>
/// Resolves which <see cref="EconomicIndex"/> is actually active for a period, following a
/// discontinued index's successor chain (design Decision 7). Pure domain logic: the caller
/// supplies every index the chain might touch, already loaded in memory — resolution never
/// queries anything itself.
/// </summary>
public static class IndexResolver
{
    private const int MaxHops = 8;

    /// <summary>
    /// Walks <paramref name="index"/>'s successor chain while <paramref name="period"/> is at
    /// or after each visited index's discontinuation date, up to <see cref="MaxHops"/> hops.
    /// The hop cap turns a data cycle into a loud, bounded failure rather than a hang.
    /// </summary>
    public static IndexResolution Resolve(
        EconomicIndex index,
        IndexPeriod period,
        IReadOnlyDictionary<Guid, EconomicIndex> catalog)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(catalog);

        var current = index;
        var hopsPerformed = 0;

        while (true)
        {
            if (current.DiscontinuedFrom is null || period < current.DiscontinuedFrom.Value)
            {
                return new IndexResolution.Resolved(current);
            }

            if (hopsPerformed >= MaxHops)
            {
                return new IndexResolution.Unresolved(
                    $"Successor chain from '{index.Name}' exceeded {MaxHops} hops without resolving.");
            }

            if (current.SuccessorIndexId is not { } successorId)
            {
                return new IndexResolution.Unresolved(
                    $"'{current.Name}' is discontinued from {current.DiscontinuedFrom} and names no successor.");
            }

            if (!catalog.TryGetValue(successorId, out var successor))
            {
                return new IndexResolution.Unresolved(
                    $"'{current.Name}' names successor '{successorId}', which could not be found.");
            }

            current = successor;
            hopsPerformed++;
        }
    }
}
