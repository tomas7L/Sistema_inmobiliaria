using System.Text.Json;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Spec test 7 (design section 10, Decision 33): the recargo terms come from the configuration and from
/// nowhere else. No container; the shipped <c>appsettings.json</c> is read the way
/// <see cref="CompositionGuardTests"/> finds files, by walking to the repository root, because
/// <c>Inmobiliaria.Desktop</c> is not referenced from here.
/// </summary>
public sealed class RecargoSettingsTests
{
    private static RecargoSettings Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RecargoSettings.Read(document.RootElement);
    }

    [Fact]
    public void TheShippedConfiguration_YieldsTwoPercentPerDayAndNoGraceDays()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "src", "Inmobiliaria.Desktop", "appsettings.json");

        var settings = Read(File.ReadAllText(path));

        Assert.Null(settings.Problem);
        Assert.NotNull(settings.Terms);
        Assert.Equal(0.02m, settings.Terms.DailyRate);
        Assert.Equal(0, settings.Terms.GraceDays);
    }

    [Theory]
    [InlineData("""{ "Recargo": { "GraceDays": 0 } }""", "Recargo:DailyRate")]
    [InlineData("""{ "Recargo": { "DailyRate": "mucho", "GraceDays": 0 } }""", "Recargo:DailyRate")]
    [InlineData("""{ "Recargo": { "DailyRate": 2, "GraceDays": 0 } }""", "Recargo:DailyRate")]
    [InlineData("""{ "Recargo": { "DailyRate": -0.01, "GraceDays": 0 } }""", "Recargo:DailyRate")]
    [InlineData("""{ "Recargo": { "DailyRate": 0.02 } }""", "Recargo:GraceDays")]
    [InlineData("""{ "Recargo": { "DailyRate": 0.02, "GraceDays": -1 } }""", "Recargo:GraceDays")]
    [InlineData("""{ "Recargo": { "DailyRate": 0.02, "GraceDays": 1.5 } }""", "Recargo:GraceDays")]
    [InlineData("""{ "Recargo": { "DailyRate": 0.02, "GraceDays": "cero" } }""", "Recargo:GraceDays")]
    [InlineData("""{ "Supabase": {} }""", "Recargo:DailyRate")]
    public void AMissingNonNumericOrOutOfRangeValue_ProducesOneProblemNamingTheKey_AndNoTerms(string json, string key)
    {
        var settings = Read(json);

        Assert.Null(settings.Terms);
        Assert.NotNull(settings.Problem);
        Assert.Contains(key, settings.Problem);
    }

    [Fact]
    public void ARateOfExactlyOne_AndZeroGraceDays_AreAccepted()
    {
        var settings = Read("""{ "Recargo": { "DailyRate": 1, "GraceDays": 0 } }""");

        Assert.Null(settings.Problem);
        Assert.Equal(1m, settings.Terms!.DailyRate);
    }

    [Fact]
    public void NoFallbackExists_AnInvalidConfigurationNeverYieldsTerms()
    {
        var settings = Read("{}");

        Assert.Null(settings.Terms);
        Assert.False(settings.IsValid);
    }
}
