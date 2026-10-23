using System.Text.Json;
using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// ui-de-cobranza spec test 24c and task 6.9 on the shipped files: the Desktop <c>appsettings.json</c> carries
/// every letterhead item, and the logo it names is in the repository. Read from the source tree, so the Linux
/// job checks it without building Desktop.
/// </summary>
public class ShippedAgencySettingsTests
{
    private static string DesktopFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Inmobiliaria.sln")))
            {
                return Path.Combine(dir.FullName, "src", "Inmobiliaria.Desktop");
            }
        }

        throw new InvalidOperationException("The repository root was not found above the test binaries.");
    }

    [Fact]
    public void TheShippedAgencySection_IsComplete_AndItsLogoExists()
    {
        var folder = DesktopFolder();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "appsettings.json")));
        var values = document.RootElement.GetProperty(AgencyDetails.Section)
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString());

        var read = AgencyDetails.Read(values);

        Assert.Empty(read.Missing);
        Assert.True(File.Exists(Path.Combine(folder, read.Details!.LogoFile)), $"Missing logo file {read.Details.LogoFile}.");
    }
}
