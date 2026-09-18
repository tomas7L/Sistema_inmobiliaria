using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Domain.Tests;

public class IndexResolverTests
{
    [Fact]
    public void Resolve_OneSuccessorHop_ResolvesThroughSuccessor()
    {
        // Spec test 15 / scenario "Resolution follows one successor hop".
        var successor = new EconomicIndex(Guid.NewGuid(), "IPC (rebased)");
        var original = new EconomicIndex(Guid.NewGuid(), "IPC");
        original.MarkDiscontinued(new IndexPeriod(2027, 1), successor.Id);

        var catalog = new Dictionary<Guid, EconomicIndex>
        {
            [original.Id] = original,
            [successor.Id] = successor,
        };

        var result = IndexResolver.Resolve(original, new IndexPeriod(2027, 2), catalog);

        var resolved = Assert.IsType<IndexResolution.Resolved>(result);
        Assert.Same(successor, resolved.Index);
    }

    [Fact]
    public void Resolve_PeriodBeforeDiscontinuation_UsesOriginalIndex()
    {
        // Scenario "Resolution before discontinuation uses the original index".
        var successor = new EconomicIndex(Guid.NewGuid(), "IPC (rebased)");
        var original = new EconomicIndex(Guid.NewGuid(), "IPC");
        original.MarkDiscontinued(new IndexPeriod(2027, 1), successor.Id);

        var catalog = new Dictionary<Guid, EconomicIndex>
        {
            [original.Id] = original,
            [successor.Id] = successor,
        };

        var result = IndexResolver.Resolve(original, new IndexPeriod(2026, 11), catalog);

        var resolved = Assert.IsType<IndexResolution.Resolved>(result);
        Assert.Same(original, resolved.Index);
    }

    [Fact]
    public void Resolve_DiscontinuedWithNoSuccessor_ReportsUnresolved()
    {
        var original = new EconomicIndex(Guid.NewGuid(), "RIPTE");
        original.MarkDiscontinued(new IndexPeriod(2027, 4));

        var catalog = new Dictionary<Guid, EconomicIndex> { [original.Id] = original };

        var result = IndexResolver.Resolve(original, new IndexPeriod(2027, 5), catalog);

        Assert.IsType<IndexResolution.Unresolved>(result);
    }

    [Fact]
    public void Resolve_SuccessorChainExceedsHopCap_ReportsUnresolvedInstead()
    {
        // A data cycle must fail loudly and bounded, never hang (design Decision 7).
        var indices = new List<EconomicIndex>();

        for (var i = 0; i < 10; i++)
        {
            indices.Add(new EconomicIndex(Guid.NewGuid(), $"Index {i}"));
        }

        for (var i = 0; i < indices.Count; i++)
        {
            var successorId = indices[(i + 1) % indices.Count].Id;
            indices[i].MarkDiscontinued(new IndexPeriod(2020, 1), successorId);
        }

        var catalog = indices.ToDictionary(i => i.Id);

        var result = IndexResolver.Resolve(indices[0], new IndexPeriod(2027, 1), catalog);

        Assert.IsType<IndexResolution.Unresolved>(result);
    }
}
