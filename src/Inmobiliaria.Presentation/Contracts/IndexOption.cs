using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>An index a clause can choose, named as the catalog names it.</summary>
public sealed record IndexOption(Guid Id, string Name)
{
    /// <summary>Every index's name by its id, discontinued ones included, so a clause that has one still reads it.</summary>
    public static IReadOnlyDictionary<Guid, string> Names(IEnumerable<IndexListing> catalog) =>
        catalog.ToDictionary(i => i.Id, i => i.Name);

    /// <summary>The indices still published, and any the clause already has, in the catalog's order.</summary>
    public static IEnumerable<IndexOption> Offered(IEnumerable<IndexListing> catalog, IReadOnlyCollection<Guid> alreadyChosen) =>
        catalog
            .Where(i => i.DiscontinuedFrom is null || alreadyChosen.Contains(i.Id))
            .Select(i => new IndexOption(i.Id, i.Name));
}
