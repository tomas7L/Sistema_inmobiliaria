using System.Text.Json;
using Inmobiliaria.Infrastructure.IndexSources;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>gestion-de-alquileres design D22, §5: the sources' addresses as configuration, read like <see cref="Access.RecargoSettings"/>.</summary>
public class IndexSourceSettingsTests
{
    private static IndexSourceSettings Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return IndexSourceSettings.Read(document.RootElement);
    }

    [Fact]
    public void TheShippedConfiguration_NamesBothOfficialSources()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "src", "Inmobiliaria.Desktop", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var settings = IndexSourceSettings.Read(document.RootElement);

        Assert.Null(settings.Problem);
        Assert.Equal("https://api.bcra.gob.ar/estadisticas/v4.0", settings.Bcra);
        Assert.Equal("https://apis.datos.gob.ar/series/api", settings.DatosGob);
    }

    [Fact]
    public void ATrailingSlash_IsDropped_SoTheRequestHasNoDoubleSlash()
    {
        var settings = Read("""{ "IndexSources": { "Bcra": "https://bcra.test/v4/", "DatosGob": "https://datos.test/api/" } }""");

        Assert.Equal("https://bcra.test/v4", settings.Bcra);
        Assert.Equal("https://datos.test/api", settings.DatosGob);
    }

    [Theory]
    [InlineData("{}", "IndexSources:Bcra")]
    [InlineData("""{ "IndexSources": { "DatosGob": "https://datos.test" } }""", "IndexSources:Bcra")]
    [InlineData("""{ "IndexSources": { "Bcra": 5, "DatosGob": "https://datos.test" } }""", "IndexSources:Bcra")]
    [InlineData("""{ "IndexSources": { "Bcra": "not an address", "DatosGob": "https://datos.test" } }""", "IndexSources:Bcra")]
    [InlineData("""{ "IndexSources": { "Bcra": "http://bcra.test", "DatosGob": "https://datos.test" } }""", "IndexSources:Bcra")]
    [InlineData("""{ "IndexSources": { "Bcra": "https://bcra.test" } }""", "IndexSources:DatosGob")]
    [InlineData("""{ "IndexSources": { "Bcra": "https://bcra.test", "DatosGob": "" } }""", "IndexSources:DatosGob")]
    public void AnInvalidAddress_IsAProblemNamingTheKey_AndLeavesNoAddress(string json, string key)
    {
        var settings = Read(json);

        Assert.Contains(key, settings.Problem, StringComparison.Ordinal);
        Assert.Null(settings.Bcra);
        Assert.Null(settings.DatosGob);
    }
}
