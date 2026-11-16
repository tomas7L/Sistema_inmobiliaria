using System.Xml.Linq;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// The desktop theme's dictionaries are merged into one, so a key used twice resolves to whichever was merged last. A
/// style named like a palette brush (both were "Theme.Select") made every hover that paints that brush throw at run time,
/// which no build or other test sees. The theme is read here as plain XML, so the check runs in the Linux CI job too.
/// </summary>
public class ThemeKeysTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EveryKeyOfTheTheme_IsUsedOnce_AcrossItsDictionaries()
    {
        var themes = Path.Combine(RepositoryRoot(), "src", "Inmobiliaria.Desktop", "Themes");
        var files = Directory.GetFiles(themes, "*.xaml");
        Assert.NotEmpty(files);

        var repeated = files
            .SelectMany(f => XDocument.Load(f).Descendants()
                .Select(e => e.Attribute(Xaml + "Key")?.Value)
                .OfType<string>()
                .Select(key => (Key: key, File: Path.GetFileName(f))))
            .GroupBy(k => k.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({string.Join(", ", g.Select(k => k.File))})")
            .ToList();

        Assert.Empty(repeated);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inmobiliaria.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Inmobiliaria.sln not found above the test binaries.");
    }
}
