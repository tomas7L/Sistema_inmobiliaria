namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Shared filesystem discovery for the lexical/structural guards that need to read source
/// files at test time (design Decision 7's source guard; spec test 2's connection-count
/// guard). Additive test infrastructure, not a task-named production file.
/// </summary>
internal static class RepoPaths
{
    public static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inmobiliaria.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (Inmobiliaria.sln not found).");
        }

        return directory.FullName;
    }
}
