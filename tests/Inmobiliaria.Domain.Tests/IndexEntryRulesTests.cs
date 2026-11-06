using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-alquileres spec Tests 32 and 33 (Requirement 12) and design D23, on the rules alone.</summary>
public class IndexEntryRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static readonly IndexPeriod August = new(2026, 8);
    private static readonly IndexValueListing Earlier = new(Guid.NewGuid(), new IndexPeriod(2026, 7), 9_000m);

    [Fact]
    public void AMonthThatHasNotBegun_IsRefused()
    {
        Assert.Equal(IndexRefusal.FuturePeriod, IndexEntryRules.CheckPeriod(new IndexPeriod(2026, 12), Today));
    }

    [Theory]
    [InlineData(2026, 11)]
    [InlineData(2026, 10)]
    [InlineData(2020, 1)]
    public void TheCurrentMonthOrAnEarlierOne_IsAllowed(int year, int month)
    {
        Assert.Null(IndexEntryRules.CheckPeriod(new IndexPeriod(year, month), Today));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ALevelThatIsNotPositive_IsRefused(string level)
    {
        Assert.Equal(IndexRefusal.LevelNotPositive, IndexEntryRules.CheckLevel(decimal.Parse(level, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void SevenDecimals_AreRefused_SinceTheColumnWouldRoundThem()
    {
        Assert.Equal(IndexRefusal.TooManyDecimals, IndexEntryRules.CheckLevel(1.1234567m));
    }

    [Theory]
    [InlineData("1.123456")]
    [InlineData("1.1234560")]
    [InlineData("999999999999.999999")]
    [InlineData("1915878.76")]
    public void ALevelTheColumnHoldsExactly_IsAllowed(string level)
    {
        Assert.Null(IndexEntryRules.CheckLevel(decimal.Parse(level, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ThirteenWholeDigits_AreRefused_SinceTheColumnWouldOverflow()
    {
        Assert.Equal(IndexRefusal.LevelTooLarge, IndexEntryRules.CheckLevel(1_000_000_000_000m));
    }

    [Fact]
    public void AnExtraZero_IsFar()
    {
        Assert.True(IndexEntryRules.IsFar(90_000m, August, Earlier));
    }

    [Fact]
    public void ALostZero_IsFar()
    {
        Assert.True(IndexEntryRules.IsFar(900m, August, Earlier));
    }

    [Fact]
    public void ExactlyTwentyFivePercentInOneMonth_IsNotFar_AndAnythingBeyondIs()
    {
        Assert.False(IndexEntryRules.IsFar(11_250m, August, Earlier));
        Assert.True(IndexEntryRules.IsFar(11_250.01m, August, Earlier));
    }

    [Fact]
    public void TheThreshold_GrowsWithTheMonthsElapsed()
    {
        var october = new IndexPeriod(2026, 10);

        Assert.False(IndexEntryRules.IsFar(15_750m, october, Earlier));
        Assert.True(IndexEntryRules.IsFar(15_750.01m, october, Earlier));
    }

    [Fact]
    public void WithNoEarlierValue_NothingIsFar()
    {
        Assert.False(IndexEntryRules.IsFar(90_000m, August, null));
    }

    [Fact]
    public void TheNearestEarlierValue_IsTheLatestBeforeThePeriod_NotALaterOne_NorItsOwn()
    {
        var june = new IndexValueListing(Guid.NewGuid(), new IndexPeriod(2026, 6), 8_000m);
        var own = new IndexValueListing(Guid.NewGuid(), August, 9_500m);
        var later = new IndexValueListing(Guid.NewGuid(), new IndexPeriod(2026, 9), 10_000m);

        Assert.Equal(Earlier, IndexEntryRules.NearestEarlier(August, [later, june, own, Earlier]));
        Assert.Null(IndexEntryRules.NearestEarlier(new IndexPeriod(2026, 5), [later, june, own, Earlier]));
    }

    [Fact]
    public void EveryRefusal_HasItsOwnOperatorText()
    {
        var texts = Enum.GetValues<IndexRefusal>().Select(r => r.OperatorText()).ToList();

        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }
}
