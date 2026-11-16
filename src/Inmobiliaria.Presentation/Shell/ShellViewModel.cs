using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Presentation.Shell;

/// <summary>
/// What the main window shows after login (spec Requirements 1 and 2, design D1): who is working, and
/// the screens that role may use. It holds no business rule and no port; each screen's view-model
/// arrives with its slice. Hiding the go-live entry from an Empleado is a convenience, never the
/// enforcement: the database refuses what an Empleado reaches by any other means.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IReadOnlyList<IPendingWork> _pendingWork;

    public ShellViewModel(IUserSession session, IReadOnlyList<string> settingsProblems, IReadOnlyList<IPendingWork>? pendingWork = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settingsProblems);

        DisplayName = session.DisplayName;
        RoleText = session.Role.ToString();
        SettingsProblems = settingsProblems;
        Navigation = BuildNavigation(session.Role, settingsProblems);
        _pendingWork = pendingWork ?? [];
        LogOutCommand = new RelayCommand(LogOut);
        ConfirmLogOutCommand = new RelayCommand(EndSession);
        CancelLogOutCommand = new RelayCommand(() => LogOutWarning = null);
    }

    /// <summary>The sidebar's last entry, for both roles (12.3d; Mateo's guide: it returns to the login).</summary>
    public string LogOutText => "Cerrar sesión";

    /// <summary>
    /// Asks to end the session. With nothing pending it ends at once, as Mateo's guide says; with work pending on any
    /// screen it shows <see cref="LogOutWarning"/> first (Tomás, 2026-11-16). The shell holds no session to dispose: the
    /// application does it.
    /// </summary>
    public IRelayCommand LogOutCommand { get; }

    public IRelayCommand ConfirmLogOutCommand { get; }

    public IRelayCommand CancelLogOutCommand { get; }

    /// <summary>Each pending work named once, then the question; null when no confirmation is asked.</summary>
    [ObservableProperty]
    private string? _logOutWarning;

    /// <summary>Raised once per <see cref="LogOutCommand"/>; the application closes the window and returns to the login.</summary>
    public event EventHandler? LogOutRequested;

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

            // Both roles; the notices' figure is the desk's, so the same terms close it (notificaciones spec Requirement 8).
            new(NavigationTarget.Notices, NavigationTitles.Notices, termsReason is null, termsReason),

            // Both roles, in the order of Mateo's sidebar (gestion-de-alquileres spec Requirement 22). Open whatever
            // the recargo terms say: those close only the screens that charge or show a recargo.
            new(NavigationTarget.Contracts, NavigationTitles.Contracts),
            new(NavigationTarget.Parties, NavigationTitles.Parties),
            new(NavigationTarget.Units, NavigationTitles.Units),
            new(NavigationTarget.Indices, NavigationTitles.Indices),
            new(NavigationTarget.Adjustments, NavigationTitles.Adjustments),
        };

        if (role == UserRole.Admin)
        {
            entries.Add(new NavigationEntry(NavigationTarget.GoLive, NavigationTitles.GoLive));
            entries.Add(new NavigationEntry(NavigationTarget.Users, NavigationTitles.Users));
        }

        return entries;
    }

    private void LogOut()
    {
        var pending = _pendingWork.Select(work => work.PendingWork).OfType<string>().Distinct().ToList();
        if (pending.Count == 0)
        {
            EndSession();
            return;
        }

        LogOutWarning = string.Join(" ", pending) + " " + PendingWorkTexts.Question;
    }

    private void EndSession()
    {
        LogOutWarning = null;
        LogOutRequested?.Invoke(this, EventArgs.Empty);
    }
}
