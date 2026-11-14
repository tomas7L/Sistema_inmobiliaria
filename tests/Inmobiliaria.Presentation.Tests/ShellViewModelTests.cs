using Inmobiliaria.Domain.Access;
using Inmobiliaria.Presentation.Shell;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>Spec tests 1 and 2 (design D1): who is working, and which screens that role is offered.</summary>
public sealed class ShellViewModelTests
{
    private static IUserSession SessionOf(string username, string displayName, UserRole role) =>
        new UserSession(new AppUser(Guid.NewGuid(), username, displayName), role);

    private static ShellViewModel ShellFor(UserRole role, params string[] settingsProblems) =>
        new(SessionOf(role == UserRole.Admin ? "nicolas" : "maria", role == UserRole.Admin ? "Nicolás Demaría" : "María Test", role),
            settingsProblems);

    private static NavigationTarget[] TargetsOf(ShellViewModel shell) =>
        shell.Navigation.Select(entry => entry.Target).ToArray();

    /// <summary>gestion-de-alquileres Requirement 22, in the order of Mateo's sidebar.</summary>
    private static readonly NavigationTarget[] RentalScreens =
    [
        NavigationTarget.Contracts, NavigationTarget.Parties, NavigationTarget.Units,
        NavigationTarget.Indices, NavigationTarget.Adjustments,
    ];

    [Fact]
    public void TheShell_ShowsTheDisplayNameAndRoleOfTheSessionItWasBuiltFrom()
    {
        var shell = new ShellViewModel(SessionOf("nicolas", "Nicolás Demaría", UserRole.Admin), []);

        Assert.Equal("Nicolás Demaría", shell.DisplayName);
        Assert.Equal("Admin", shell.RoleText);
    }

    [Fact]
    public void AnEmpleado_ShowsTheEmpleadoRole()
    {
        var shell = ShellFor(UserRole.Empleado);

        Assert.Equal("Empleado", shell.RoleText);
    }

    [Fact]
    public void AnEmpleadosNavigation_HasTheDeskTheRecentListVoidingAvisosAndTheRentalScreens_AndNoGoLiveOrUsers()
    {
        var shell = ShellFor(UserRole.Empleado);

        Assert.Equal(
            [
                NavigationTarget.CashDesk, NavigationTarget.RecentCollections, NavigationTarget.Voiding,
                NavigationTarget.Notices, .. RentalScreens,
            ],
            TargetsOf(shell));
        Assert.DoesNotContain(NavigationTarget.GoLive, TargetsOf(shell));
        Assert.DoesNotContain(NavigationTarget.Users, TargetsOf(shell));
    }

    /// <summary>gestion-de-usuarios spec Test 20: an Admin also has Usuarios.</summary>
    [Fact]
    public void AnAdminsNavigation_HasEveryScreen()
    {
        var shell = ShellFor(UserRole.Admin);

        Assert.Equal(
            [
                NavigationTarget.CashDesk, NavigationTarget.RecentCollections, NavigationTarget.Voiding,
                NavigationTarget.Notices, .. RentalScreens, NavigationTarget.GoLive, NavigationTarget.Users,
            ],
            TargetsOf(shell));
        Assert.Equal("Usuarios", shell.Navigation.Single(e => e.Target == NavigationTarget.Users).Title);
    }

    /// <summary>
    /// gestion-de-alquileres spec Test 70: both roles get all five rental screens, open, titled as Mateo's sidebar
    /// titles them, even when the recargo terms close the desk; Usuarios stays Admin-only.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Empleado)]
    [InlineData(UserRole.Admin)]
    public void BothRoles_GetTheFiveRentalScreens_OpenEvenWithInvalidRecargoTerms(UserRole role)
    {
        var shell = ShellFor(role, "Recargo:DailyRate falta.");

        var rental = shell.Navigation.Where(entry => RentalScreens.Contains(entry.Target)).ToArray();

        Assert.Equal(RentalScreens, rental.Select(entry => entry.Target));
        Assert.Equal(
            ["Contratos", "Personas", "Propiedades", "Índices", "Ajustes de alquiler"],
            rental.Select(entry => entry.Title));
        Assert.All(rental, entry => Assert.True(entry.IsAvailable));
        Assert.Equal(role == UserRole.Admin, TargetsOf(shell).Contains(NavigationTarget.Users));
    }

    [Fact]
    public void WithValidSettings_EveryEntryIsAvailable()
    {
        var shell = ShellFor(UserRole.Admin);

        Assert.All(shell.Navigation, entry =>
        {
            Assert.True(entry.IsAvailable);
            Assert.Null(entry.UnavailableReason);
        });
        Assert.Empty(shell.SettingsProblems);
    }

    [Fact]
    public void WithInvalidRecargoTerms_TheDeskAndVoidingAreUnavailable_NamingTheSetting_AndTheOthersStillOpen()
    {
        var shell = ShellFor(UserRole.Admin, "Recargo:DailyRate falta.");

        var byTarget = shell.Navigation.ToDictionary(entry => entry.Target);
        foreach (var blocked in new[] { NavigationTarget.CashDesk, NavigationTarget.Voiding })
        {
            Assert.False(byTarget[blocked].IsAvailable);
            Assert.Contains("Recargo:DailyRate", byTarget[blocked].UnavailableReason);
        }

        Assert.True(byTarget[NavigationTarget.RecentCollections].IsAvailable);
        Assert.True(byTarget[NavigationTarget.GoLive].IsAvailable);
        Assert.Equal(["Recargo:DailyRate falta."], shell.SettingsProblems);
    }

    /// <summary>
    /// notificaciones spec Test 16: both roles reach Avisos, and with invalid recargo settings it is disabled with Caja de
    /// cobranza's reason, because the desk figure needs them. Only an Admin is offered the mailbox setup: see
    /// <c>NoticesViewModelTests.OnlyAnAdmin_IsOfferedTheMailboxSetup</c>.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Empleado)]
    public void BothRolesReachAvisos_AndInvalidRecargoTermsDisableItWithTheDesksReason(UserRole role)
    {
        var valid = ShellFor(role).Navigation.Single(e => e.Target == NavigationTarget.Notices);
        Assert.Equal(("Avisos", true, null), (valid.Title, valid.IsAvailable, valid.UnavailableReason));

        var byTarget = ShellFor(role, "Recargo:DailyRate falta.").Navigation.ToDictionary(entry => entry.Target);
        Assert.False(byTarget[NavigationTarget.Notices].IsAvailable);
        Assert.Equal(byTarget[NavigationTarget.CashDesk].UnavailableReason, byTarget[NavigationTarget.Notices].UnavailableReason);
    }
}
