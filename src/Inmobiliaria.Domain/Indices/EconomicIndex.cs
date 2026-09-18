namespace Inmobiliaria.Domain.Indices;

/// <summary>
/// Catalog entry for a named economic index (e.g. IPC, RIPTE, ICL) that a contract's
/// adjustment clause can reference. Discontinuing an index never deletes or alters its
/// previously published <see cref="IndexValue"/> rows; it only affects which index
/// <see cref="IndexResolver"/> resolves to for later periods.
/// </summary>
public sealed class EconomicIndex
{
    public Guid Id { get; }
    public string Name { get; }
    public IndexPeriod? DiscontinuedFrom { get; private set; }
    public Guid? SuccessorIndexId { get; private set; }

    public EconomicIndex(Guid id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
    }

    /// <summary>
    /// Marks this index discontinued from <paramref name="period"/> onward, optionally naming
    /// a successor of similar characteristics. A successor may be omitted — spec scenario
    /// "Discontinued index without a successor yet" — leaving later resolution to fail
    /// explicitly rather than silently.
    /// </summary>
    public void MarkDiscontinued(IndexPeriod period, Guid? successorIndexId = null)
    {
        if (successorIndexId == Id)
        {
            throw new ArgumentException(
                "An index cannot name itself as its own successor.",
                nameof(successorIndexId));
        }

        DiscontinuedFrom = period;
        SuccessorIndexId = successorIndexId;
    }
}
