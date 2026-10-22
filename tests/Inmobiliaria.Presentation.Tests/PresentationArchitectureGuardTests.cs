using System.Reflection;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// Mechanical half of the Presentation boundary (design D1): view-models and pure presentation
/// functions know ports, never an adapter and never a UI toolkit. The Linux `core` CI job already
/// fails on a WPF reference at build time; Infrastructure, EF Core and Npgsql all build fine on
/// Linux, so this reflection-based guard is what catches those. Modelled on Domain.Tests'
/// <c>ArchitectureGuardTests</c>, with Infrastructure added to the forbidden prefixes.
/// </summary>
public class PresentationArchitectureGuardTests
{
    private static readonly string[] ForbiddenAssemblyNamePrefixes =
    [
        "Inmobiliaria.Infrastructure",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
    ];

    [Fact]
    public void PresentationAssembly_ReferencesNoInfrastructureOrUiAssembly()
    {
        var referencedAssemblies = Assembly.Load("Inmobiliaria.Presentation").GetReferencedAssemblies();

        var forbidden = referencedAssemblies
            .Where(assemblyName =>
                assemblyName.Name is not null &&
                ForbiddenAssemblyNamePrefixes.Any(prefix =>
                    assemblyName.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(assemblyName => assemblyName.Name)
            .ToList();

        Assert.Empty(forbidden);
    }
}
