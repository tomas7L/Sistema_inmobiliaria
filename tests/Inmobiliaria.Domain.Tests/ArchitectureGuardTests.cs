using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// Mechanical half of the Domain -> WPF/EF boundary (design Decision 1). The Linux
/// `core` CI job catches a WPF reference (fails to build on Linux); EF Core builds
/// fine on Linux, so this reflection-based guard is what catches that half.
/// </summary>
public class ArchitectureGuardTests
{
    private static readonly string[] ForbiddenAssemblyNamePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
    ];

    [Fact]
    public void DomainAssembly_ReferencesNoInfrastructureOrUiAssembly()
    {
        var referencedAssemblies = typeof(Party).Assembly.GetReferencedAssemblies();

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
