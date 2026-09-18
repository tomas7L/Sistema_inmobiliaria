namespace Inmobiliaria.Domain.Indices;

/// <summary>
/// Outcome of <see cref="IndexResolver.Resolve"/>: either the index actually active for the
/// requested period, or an explicit reason none could be found. Never an exception — "no
/// value can be resolved" is a worklist outcome (pending), not a fault (design Decision 7).
/// </summary>
public abstract record IndexResolution
{
    private IndexResolution()
    {
    }

    public sealed record Resolved(EconomicIndex Index) : IndexResolution;

    public sealed record Unresolved(string Reason) : IndexResolution;
}
