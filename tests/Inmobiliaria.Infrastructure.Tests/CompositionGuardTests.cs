using Inmobiliaria.Infrastructure.Access;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Proves design Decision 7's two no-container guards, both of which run in the Linux `core`
/// job on every PR because <c>Inmobiliaria.Desktop</c> itself is excluded from
/// <c>Inmobiliaria.Core.slnf</c> and cannot gate anything through it. No Docker/Testcontainers
/// involved anywhere in this file — every fact here is proven by reflection or plain file
/// reads (spec tests 1, 6; users-and-roles tasks 4.9, 4.10).
/// </summary>
public sealed class CompositionGuardTests
{
    /// <summary>Spec test 1 (configuration half). No known credential-shaped key appears in any committed <c>appsettings*.json</c>.</summary>
    [Fact]
    public void NoDatabaseCredential_FoundInConfigurationFiles()
    {
        var repoRoot = RepoPaths.FindRepoRoot();

        var configFiles = Directory.EnumerateFiles(repoRoot, "appsettings*.json", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        string[] suspiciousTokens = ["Password", "ConnectionString", "Pwd="];

        foreach (var file in configFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var token in suspiciousTokens)
            {
                Assert.DoesNotContain(token, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Spec test 1 (runtime-environment half). The process environment carries nothing that looks like a database credential.</summary>
    [Fact]
    public void NoDatabaseCredential_FoundInTheRuntimeEnvironment()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key)
            {
                continue;
            }

            // INMOBILIARIA_DB itself is allowed to exist (it is DesignTimeDbContextFactory's
            // own, explicitly documented, development-time-only input — design Decision 7) but
            // nothing named like an ordinary database password variable should be present.
            Assert.DoesNotContain("DB_PASSWORD", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("POSTGRES_PASSWORD", key, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Spec test 6 / design Decision 7's container guard. Builds the exact pre-login
    /// <see cref="IServiceCollection"/> and asserts no descriptor's service or implementation
    /// type is assignable to anything DB-shaped — "no database access before authentication"
    /// is a property of this container's contents, not a rule anybody could forget to write.
    /// </summary>
    [Fact]
    public void PreLoginServiceCollection_RegistersNothingDatabaseShaped()
    {
        var services = new ServiceCollection();
        var endpoint = new ConnectionEndpoint("localhost", 5432, "inmobiliaria", "test-ref", SslMode.Disable);

        services.AddPreLoginServices(endpoint);

        Type[] forbiddenTypes =
        [
            typeof(DbContext),
            typeof(NpgsqlDataSource),
            typeof(NpgsqlConnection),
            typeof(ISessionDbContextFactory),
        ];

        foreach (var descriptor in services)
        {
            foreach (var forbidden in forbiddenTypes)
            {
                Assert.False(
                    forbidden.IsAssignableFrom(descriptor.ServiceType),
                    $"Service type {descriptor.ServiceType} is assignable to forbidden type {forbidden}.");

                if (descriptor.ImplementationType is not null)
                {
                    Assert.False(
                        forbidden.IsAssignableFrom(descriptor.ImplementationType),
                        $"Implementation type {descriptor.ImplementationType} is assignable to forbidden type {forbidden}.");
                }
            }
        }
    }

    /// <summary>
    /// Task 4.10 / design Decision 7's source guard. Stated limitation carried from design.md:
    /// this is a lexical guard, not a semantic one — a Roslyn analyzer was considered and
    /// rejected as disproportionate for one rule.
    /// </summary>
    [Fact]
    public void MigrateAndDesignTimeConnectionString_AppearOnlyInTheAllowedFile()
    {
        var repoRoot = RepoPaths.FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");

        string[] forbiddenTokens = ["MigrateAsync(", "Migrate(", "INMOBILIARIA_DB"];
        string[] allowedFileNames = ["DesignTimeDbContextFactory.cs"];

        var offendingFiles = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (allowedFileNames.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            if (forbiddenTokens.Any(token => content.Contains(token, StringComparison.Ordinal)))
            {
                offendingFiles.Add(file);
            }
        }

        Assert.Empty(offendingFiles);
    }
}
