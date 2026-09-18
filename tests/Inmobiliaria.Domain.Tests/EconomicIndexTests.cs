using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Domain.Tests;

public class EconomicIndexTests
{
    [Fact]
    public void MarkDiscontinued_WithoutSuccessor_IsAccepted()
    {
        // Scenario "Discontinued index without a successor yet".
        var index = new EconomicIndex(Guid.NewGuid(), "ICL");

        index.MarkDiscontinued(new IndexPeriod(2027, 1));

        Assert.Equal(new IndexPeriod(2027, 1), index.DiscontinuedFrom);
        Assert.Null(index.SuccessorIndexId);
    }

    [Fact]
    public void MarkDiscontinued_NamingItselfAsSuccessor_IsRejected()
    {
        var index = new EconomicIndex(Guid.NewGuid(), "IPC");

        Assert.Throws<ArgumentException>(() => index.MarkDiscontinued(new IndexPeriod(2027, 1), index.Id));
    }

    [Fact]
    public void MarkDiscontinued_DoesNotAlterAlreadyStoredValues()
    {
        // Scenario "An index is discontinued with a successor": existing published values
        // must remain stored and readable unchanged.
        var index = new EconomicIndex(Guid.NewGuid(), "RIPTE");
        var value = new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2027, 3), 1_200_000m);

        index.MarkDiscontinued(new IndexPeriod(2027, 4), Guid.NewGuid());

        Assert.Equal(1_200_000m, value.Level);
        Assert.Equal(index.Id, value.EconomicIndexId);
    }

    [Fact]
    public void Constructor_RejectsBlankName()
    {
        Assert.Throws<ArgumentException>(() => new EconomicIndex(Guid.NewGuid(), " "));
    }
}
