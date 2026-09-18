using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Domain.Tests;

public class IndexValueTests
{
    [Fact]
    public void Constructor_RejectsZeroLevel()
    {
        Assert.Throws<ArgumentException>(() =>
            new IndexValue(Guid.NewGuid(), Guid.NewGuid(), new IndexPeriod(2026, 8), 0m));
    }

    [Fact]
    public void Constructor_RejectsNegativeLevel()
    {
        Assert.Throws<ArgumentException>(() =>
            new IndexValue(Guid.NewGuid(), Guid.NewGuid(), new IndexPeriod(2026, 8), -1m));
    }

    [Fact]
    public void Correct_ReplacesLevel_KeepsIdStable()
    {
        var id = Guid.NewGuid();
        var value = new IndexValue(id, Guid.NewGuid(), new IndexPeriod(2026, 8), 8_000m);

        value.Correct(8_050m);

        Assert.Equal(id, value.Id);
        Assert.Equal(8_050m, value.Level);
    }

    [Fact]
    public void Correct_RejectsNonPositiveLevel()
    {
        var value = new IndexValue(Guid.NewGuid(), Guid.NewGuid(), new IndexPeriod(2026, 8), 8_000m);

        Assert.Throws<ArgumentException>(() => value.Correct(0m));
    }
}
