using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-alquileres spec Test 71, task 12.4: an adjustment's figures are checked against what can be stored.</summary>
public class AdjustmentLimitsTests
{
    private static RentAdjustmentIndexValue Variation(decimal variation) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new IndexPeriod(2025, 12), 100m, new IndexPeriod(2026, 6), 110m, variation);

    [Theory]
    [InlineData(999_999.999999, true)]
    [InlineData(-999_999.999999, true)]
    [InlineData(1_000_000, false)]
    [InlineData(-1_000_000, false)]
    public void TheCoefficient_FitsUpToTwelveDigitsWithSix(double coefficient, bool fits) =>
        Assert.Equal(fits, AdjustmentLimits.PercentsFit((decimal)coefficient, [Variation(10m)]));

    [Fact]
    public void EachVariation_MustFitToo_NotOnlyTheirAverage()
    {
        // Averaged with a fall, an overflowing variation leaves a coefficient that fits, but the variation is stored as well.
        Assert.False(AdjustmentLimits.PercentsFit(499_950m, [Variation(1_000_000m), Variation(-100m)]));
        Assert.True(AdjustmentLimits.PercentsFit(499_950m, [Variation(999_999.999999m), Variation(-100m)]));
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("0.01", true)]
    [InlineData("999999999999.99", true)]
    [InlineData("1000000000000", false)]
    public void TheCanon_IsMoreThanZero_AndFitsFourteenDigitsWithTwo(string canon, bool fits) =>
        Assert.Equal(fits, AdjustmentLimits.CanonFits(decimal.Parse(canon, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("999999999999.99", true)]
    [InlineData("1000000000000", false)]
    public void ADifference_FitsAMovementsAmount(string amount, bool fits) =>
        Assert.Equal(fits, AdjustmentLimits.AmountFits(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
}
