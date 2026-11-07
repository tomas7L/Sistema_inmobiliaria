namespace Inmobiliaria.Domain.Indices;

/// <summary>
/// Asks an index's official source for the level of one month (gestion-de-alquileres spec Requirement 25, design §5). It stores
/// nothing: what it finds goes through <see cref="IIndexRegistry"/> once the operator confirms. Nothing an operator can run into
/// comes back as an exception, since manual entry has to stay available whatever the source does.
/// </summary>
public interface IIndexValueSource
{
    /// <summary>Whether this index has an official source to ask. False is what leaves RIPTE typed.</summary>
    bool Supports(string indexName);

    /// <param name="indexName">The index, as named in the catalog.</param>
    /// <param name="period">The month. For ICL, the source is asked for its value on the 1st (spec Decision 41).</param>
    /// <param name="ct">Cancels the request.</param>
    Task<IndexFetch> FetchAsync(string indexName, IndexPeriod period, CancellationToken ct = default);
}

/// <summary>What a source answered about one month.</summary>
public abstract record IndexFetch
{
    private IndexFetch()
    {
    }

    /// <param name="Level">The level the source publishes for exactly that month.</param>
    public sealed record Found(decimal Level) : IndexFetch;

    /// <summary>The source does not hold that month. Not an error: it runs behind the publication (spec Finding F11).</summary>
    public sealed record NotPublishedYet : IndexFetch;

    /// <param name="TimedOut">True when it stopped answering, false when it could not be reached at all.</param>
    public sealed record Unreachable(bool TimedOut) : IndexFetch;

    /// <param name="ServerError">
    /// True when it answered with an error status, false when its answer was not what it should be: another shape, the wrong month
    /// or a non-positive level.
    /// </param>
    public sealed record SourceFailed(bool ServerError) : IndexFetch;
}
