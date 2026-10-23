using Inmobiliaria.Infrastructure.Persistence;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// The account search's text rule (ui-de-cobranza design D17), with no database: case and accents are
/// ignored, fewer than two meaningful characters find nothing, and at most fifty results come back.
/// </summary>
public class SearchFoldTests
{
    [Theory]
    [InlineData("Demaría", "demaria")]
    [InlineData("MUÑOZ", "munoz")]
    [InlineData("  Pérez  ", "perez")]
    [InlineData("Bv. Belgrano 123", "bv. belgrano 123")]
    public void Folding_IgnoresCaseAccentsAndSurroundingSpace(string text, string folded) =>
        Assert.Equal(folded, AccountSearch.Fold(text));

    [Fact]
    public void DemariaWithAndWithoutTheAccent_FoldEqual() =>
        Assert.Equal(AccountSearch.Fold("Demaría"), AccountSearch.Fold("demaria"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData(" á ")]
    public void FewerThanTwoCharacters_FindNothing(string text)
    {
        var found = AccountSearch.Select(["ana", "alba"], text, s => [s], s => s);

        Assert.Empty(found);
    }

    [Fact]
    public void TwoCharacters_AreEnough_AndMatchBySubstringInAnyField()
    {
        var found = AccountSearch.Select(
            [("Demaría, Ana", "San Martín 45"), ("Gomez, Juan", "Bv. Belgrano 123")],
            "ma",
            c => [c.Item1, c.Item2],
            c => c.Item1);

        Assert.Equal([("Demaría, Ana", "San Martín 45")], found);
    }

    [Fact]
    public void AtMostFiftyResults_SortedBySortKey()
    {
        var candidates = Enumerable.Range(0, 60).Select(i => $"inquilino {59 - i:00}").ToList();

        var found = AccountSearch.Select(candidates, "inquilino", s => [s], s => s);

        Assert.Equal(AccountSearch.MaxResults, found.Count);
        Assert.Equal(50, found.Count);
        Assert.Equal("inquilino 00", found[0]);
        Assert.Equal("inquilino 49", found[^1]);
    }

    /// <summary>S3a review R3-search-tie-order-nondeterministic: equal sort keys come back in the tie-break's order.</summary>
    [Fact]
    public void EqualSortKeys_AreOrderedByTheTieBreak()
    {
        var found = AccountSearch.Select(
            [("Perez, Maria", "b"), ("PÉREZ, MARÍA", "a"), ("Perez, Maria", "c")],
            "perez",
            c => [c.Item1],
            c => c.Item1,
            c => c.Item2);

        Assert.Equal(["a", "b", "c"], found.Select(c => c.Item2));
    }
}
