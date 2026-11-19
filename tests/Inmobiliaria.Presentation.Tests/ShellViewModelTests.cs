using Inmobiliaria.Domain.Access;
using Inmobiliaria.Presentation.Common;
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

    /// <summary>The canvas's ".usr", at the top right of every screen: the name and the role in the operator's words.</summary>
    [Fact]
    public void TheHeader_NamesTheUserAndTheirRoleInSpanish()
    {
        Assert.Equal("Nicolás Demaría, Administrador", ShellFor(UserRole.Admin).HeaderText);
        Assert.Equal("María Test, Empleado", ShellFor(UserRole.Empleado).HeaderText);
    }

    /// <summary>The canvas's sidebar groups its entries under "Cobranza", "Alquileres" and "Administración".</summary>
    [Fact]
    public void EachEntry_BelongsToItsSidebarGroup()
    {
        var groups = ShellFor(UserRole.Admin).Navigation.Select(entry => (entry.Target, entry.Group)).ToArray();

        Assert.Equal(
            [
                (NavigationTarget.CashDesk, "Cobranza"), (NavigationTarget.RecentCollections, "Cobranza"),
                (NavigationTarget.Voiding, "Cobranza"), (NavigationTarget.Notices, "Cobranza"),
                (NavigationTarget.Contracts, "Alquileres"), (NavigationTarget.Parties, "Alquileres"),
                (NavigationTarget.Units, "Alquileres"), (NavigationTarget.Indices, "Alquileres"),
                (NavigationTarget.Adjustments, "Alquileres"),
                (NavigationTarget.GoLive, "Administración"), (NavigationTarget.Users, "Administración"),
            ],
            groups);
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

    /// <summary>12.3d, Cerrar sesión (Tomás, 2026-11-13): the shell asks to end the session; the application does it.</summary>
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Empleado)]
    public void CerrarSesion_IsOfferedToBothRoles_AndAsksOnceToEndTheSession(UserRole role)
    {
        var shell = ShellFor(role);
        var requests = 0;
        shell.LogOutRequested += (_, _) => requests++;

        Assert.Equal("Cerrar sesión", shell.LogOutText);
        Assert.True(shell.LogOutCommand.CanExecute(null));
        shell.LogOutCommand.Execute(null);

        Assert.Equal(1, requests);
    }

    private sealed class Pending(string? text) : IPendingWork
    {
        public string? PendingWork { get; set; } = text;
    }

    private static ShellViewModel ShellWith(params IPendingWork[] work) =>
        new(SessionOf("maria", "María Test", UserRole.Empleado), [], work);

    /// <summary>12.3d, Tomás 2026-11-16: with work pending, Cerrar sesión asks first and names it.</summary>
    [Fact]
    public void CerrarSesion_WithWorkPending_AsksFirst_AndNamesTheWork()
    {
        var shell = ShellWith(new Pending(null), new Pending("Hay un cobro sin confirmar."));
        var requests = 0;
        shell.LogOutRequested += (_, _) => requests++;

        shell.LogOutCommand.Execute(null);

        Assert.Equal(0, requests);
        Assert.Equal("Hay un cobro sin confirmar. ¿Cerrar sesión igual? Lo que no se confirmó se pierde.", shell.LogOutWarning);
    }

    [Fact]
    public void ConfirmingTheWarning_EndsTheSession_AndCancellingKeepsIt()
    {
        var shell = ShellWith(new Pending("Hay un cobro sin confirmar."));
        var requests = 0;
        shell.LogOutRequested += (_, _) => requests++;

        shell.LogOutCommand.Execute(null);
        shell.CancelLogOutCommand.Execute(null);

        Assert.Equal(0, requests);
        Assert.Null(shell.LogOutWarning);

        shell.LogOutCommand.Execute(null);
        shell.ConfirmLogOutCommand.Execute(null);

        Assert.Equal(1, requests);
        Assert.Null(shell.LogOutWarning);
    }

    [Fact]
    public void EveryPendingWork_IsNamedOnce()
    {
        var shell = ShellWith(
            new Pending("Hay una operación en curso."), new Pending("Hay una operación en curso."), new Pending("Hay una anulación sin confirmar."));

        shell.LogOutCommand.Execute(null);

        Assert.Equal(
            "Hay una operación en curso. Hay una anulación sin confirmar. ¿Cerrar sesión igual? Lo que no se confirmó se pierde.",
            shell.LogOutWarning);
    }

    /// <summary>A screen whose runner is still working has an operation in progress; once it ends, nothing.</summary>
    [Fact]
    public async Task ARunningOperation_IsPendingWork_UntilItEnds()
    {
        var runner = new OperationRunner();
        var work = new RunningWork(runner);
        var gate = new TaskCompletionSource<int>();

        var run = runner.RunAsync(() => gate.Task);
        Assert.Equal("Hay una operación en curso.", work.PendingWork);

        gate.SetResult(1);
        await run;
        Assert.Null(work.PendingWork);
    }
}
