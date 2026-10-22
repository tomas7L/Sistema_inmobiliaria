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
    public void AnEmpleadosNavigation_HasTheDeskTheRecentListAndVoiding_AndNoGoLive()
    {
        var shell = ShellFor(UserRole.Empleado);

        Assert.Equal(
            [NavigationTarget.CashDesk, NavigationTarget.RecentCollections, NavigationTarget.Voiding],
            TargetsOf(shell));
        Assert.DoesNotContain(NavigationTarget.GoLive, TargetsOf(shell));
    }

    [Fact]
    public void AnAdminsNavigation_HasEveryScreen()
    {
        var shell = ShellFor(UserRole.Admin);

        Assert.Equal(
            [NavigationTarget.CashDesk, NavigationTarget.RecentCollections, NavigationTarget.Voiding, NavigationTarget.GoLive],
            TargetsOf(shell));
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
}
