using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Presentation.Shell;

/// <summary>
/// What the main window shows after login (spec Requirements 1 and 2, design D1): who is working, and
/// the screens that role may use. It holds no business rule and no port; each screen's view-model
/// arrives with its slice. Hiding the go-live entry from an Empleado is a convenience, never the
/// enforcement: the database refuses what an Empleado reaches by any other means.
/// </summary>
public sealed class ShellViewModel
{
    public ShellViewModel(IUserSession session, IReadOnlyList<string> settingsProblems)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settingsProblems);

        DisplayName = session.DisplayName;
        RoleText = session.Role.ToString();
        SettingsProblems = settingsProblems;
        Navigation = BuildNavigation(session.Role, settingsProblems);
    }

    public string DisplayName { get; }

    /// <summary>The role's name as the database knows it ("Admin" or "Empleado").</summary>
    public string RoleText { get; }

    /// <summary>Configuration problems found at startup, each naming its key; empty when the settings are valid.</summary>
    public IReadOnlyList<string> SettingsProblems { get; }

    public IReadOnlyList<NavigationEntry> Navigation { get; }

    private static IReadOnlyList<NavigationEntry> BuildNavigation(UserRole role, IReadOnlyList<string> settingsProblems)
    {
        // Voiding is planned with the same recargo terms as the desk, so invalid terms close both (design D16).
        var termsReason = settingsProblems.Count == 0
            ? null
            : "No disponible hasta corregir la configuración: " + string.Join(" ", settingsProblems);

        var entries = new List<NavigationEntry>
        {
            new(NavigationTarget.CashDesk, NavigationTitles.CashDesk, termsReason is null, termsReason),
            new(NavigationTarget.RecentCollections, NavigationTitles.RecentCollections),
            new(NavigationTarget.Voiding, NavigationTitles.Voiding, termsReason is null, termsReason),
        };

        if (role == UserRole.Admin)
        {
            entries.Add(new NavigationEntry(NavigationTarget.GoLive, NavigationTitles.GoLive));
        }

        return entries;
    }
}
