using Inmobiliaria.Domain.Access;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Tests.Fakes;
using Inmobiliaria.Presentation.Users;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-usuarios spec Tests 12, 14 (view-model half), 16 and 17, with the port and the clipboard faked.</summary>
public class UsersViewModelTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid SofiaId = Guid.NewGuid();
    private static readonly Guid MariaId = Guid.NewGuid();
    private static readonly Guid NoRoleId = Guid.NewGuid();

    private static FakeUserAdministration Port() => new()
    {
        Users =
        [
            new(MariaId, "maria", "María Gómez", UserRole.Empleado, IsActive: false, MustChangePassword: false),
            new(SofiaId, "sofia", "Sofía Pérez", UserRole.Empleado, IsActive: true, MustChangePassword: true),
            new(AdminId, "nicolas", "Nicolás Demaría", UserRole.Admin, IsActive: true, MustChangePassword: false),
            new(NoRoleId, "suelto", "Álvaro Suelto", null, IsActive: true, MustChangePassword: false),
        ],
    };

    private static async Task<UsersViewModel> LoadedAsync(FakeUserAdministration port, FakeClipboard? clipboard = null)
    {
        var viewModel = new UsersViewModel(port, clipboard ?? new FakeClipboard());
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static UserRow Row(UsersViewModel viewModel, Guid id) => viewModel.Users.Single(u => u.Id == id);

    /// <summary>Spec Requirement 1 and Decision 7: active first, then by display name; each row says role, state and pending change.</summary>
    [Fact]
    public async Task TheList_IsActiveFirst_ByName_WithRoleStateAndPendingChange()
    {
        var viewModel = await LoadedAsync(Port());

        Assert.Equal(
            [
                ("Álvaro Suelto", "", "Activo", ""),
                ("Nicolás Demaría", "Administrador", "Activo", ""),
                ("Sofía Pérez", "Empleado", "Activo", "Debe cambiar la contraseña"),
                ("María Gómez", "Empleado", "Desactivado", ""),
            ],
            viewModel.Users.Select(u => (u.DisplayName, u.RoleText, u.StateText, u.PendingText)).ToList());
    }

    /// <summary>Spec Test 14, view-model half, and Requirement 3 to 5: only Empleado rows have actions, by state.</summary>
    [Fact]
    public async Task OnlyEmpleadoRows_HaveActions_AccordingToTheirState()
    {
        var viewModel = await LoadedAsync(Port());

        Assert.Equal((true, true, false), Actions(Row(viewModel, SofiaId)));
        Assert.Equal((false, false, true), Actions(Row(viewModel, MariaId)));
        Assert.Equal((false, false, false), Actions(Row(viewModel, AdminId)));
        Assert.Equal((false, false, false), Actions(Row(viewModel, NoRoleId)));
        Assert.False(viewModel.ResetCommand.CanExecute(Row(viewModel, AdminId)));
        Assert.False(viewModel.DeactivateCommand.CanExecute(Row(viewModel, AdminId)));
        Assert.False(viewModel.ReactivateCommand.CanExecute(Row(viewModel, AdminId)));

        static (bool, bool, bool) Actions(UserRow row) => (row.CanReset, row.CanDeactivate, row.CanReactivate);
    }

    /// <summary>Spec Test 17: nothing runs until confirmed, the confirmation names the person, and cancelling changes nothing.</summary>
    [Fact]
    public async Task Reset_Deactivate_AndReactivate_WaitForConfirmation()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);

        await viewModel.ResetCommand.ExecuteAsync(Row(viewModel, SofiaId));
        Assert.Empty(port.Calls);
        Assert.Contains("Sofía Pérez", viewModel.Pending!.Text);

        viewModel.CancelCommand.Execute(null);
        Assert.Null(viewModel.Pending);
        Assert.Empty(port.Calls);

        await viewModel.DeactivateCommand.ExecuteAsync(Row(viewModel, SofiaId));
        await viewModel.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal([$"deactivate {SofiaId}"], port.Calls);
        Assert.Null(viewModel.Pending);

        await viewModel.ReactivateCommand.ExecuteAsync(Row(viewModel, MariaId));
        Assert.Contains("María Gómez", viewModel.Pending!.Text);
        await viewModel.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal([$"deactivate {SofiaId}", $"reactivate {MariaId}"], port.Calls);
    }

    /// <summary>Spec Requirement 4 and Finding F1: the deactivation confirmation says an open session keeps working.</summary>
    [Fact]
    public async Task TheDeactivationConfirmation_MentionsTheOpenSession()
    {
        var viewModel = await LoadedAsync(Port());

        await viewModel.DeactivateCommand.ExecuteAsync(Row(viewModel, SofiaId));

        Assert.Contains("sigue trabajando hasta que lo cierre", viewModel.Pending!.Text);
    }

    /// <summary>Spec Test 12: a generated password is shown until dismissed, then never again.</summary>
    [Fact]
    public async Task AGeneratedPassword_IsShownUntilDismissed_ThenNeverAgain()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        await viewModel.ResetCommand.ExecuteAsync(Row(viewModel, SofiaId));

        await viewModel.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(new ProvisionalCredential("sofia", "mar-pan-sol-12345"), viewModel.Revealed);

        viewModel.DismissCommand.Execute(null);
        Assert.Null(viewModel.Revealed);
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.Null(viewModel.Revealed);
    }

    /// <summary>Spec Test 12 and Decision 12: Copy writes exactly the password, and nothing else writes the clipboard.</summary>
    [Fact]
    public async Task Copy_WritesExactlyThePassword_AndNothingElseWritesTheClipboard()
    {
        var clipboard = new FakeClipboard();
        var viewModel = await LoadedAsync(Port(), clipboard);
        viewModel.NewUsername = "lucia";
        viewModel.NewDisplayName = "Lucía Díaz";

        await viewModel.CreateCommand.ExecuteAsync(null);
        Assert.Empty(clipboard.Written);

        viewModel.CopyCommand.Execute(null);
        Assert.Equal(["lago-sol-pan-48017"], clipboard.Written);

        viewModel.DismissCommand.Execute(null);
        Assert.False(viewModel.CopyCommand.CanExecute(null));
    }

    /// <summary>Spec Requirement 6: dismissing the password also takes it off the clipboard, if it is still there.</summary>
    [Fact]
    public async Task Dismiss_TakesThePasswordOffTheClipboard()
    {
        var clipboard = new FakeClipboard();
        var viewModel = await LoadedAsync(Port(), clipboard);
        await viewModel.ResetCommand.ExecuteAsync(Row(viewModel, SofiaId));
        await viewModel.ConfirmCommand.ExecuteAsync(null);
        viewModel.CopyCommand.Execute(null);

        viewModel.DismissCommand.Execute(null);

        Assert.Equal(["mar-pan-sol-12345"], clipboard.Cleared);
    }

    /// <summary>A busy clipboard is said on screen, and the password stays shown to be copied by hand.</summary>
    [Fact]
    public async Task ACopyThatFails_IsSaid_AndThePasswordStays()
    {
        var clipboard = new FakeClipboard { Fails = true };
        var viewModel = await LoadedAsync(Port(), clipboard);
        await viewModel.ResetCommand.ExecuteAsync(Row(viewModel, SofiaId));
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        viewModel.CopyCommand.Execute(null);

        Assert.Equal("No se pudo copiar. Seleccione la contraseña y cópiela a mano.", viewModel.CopyMessage);
        Assert.NotNull(viewModel.Revealed);
        viewModel.DismissCommand.Execute(null);
        Assert.Null(viewModel.CopyMessage);
    }

    /// <summary>
    /// Spec Requirement 6: a password already set in the database cannot be lost from the screen, so no other action
    /// runs until the Admin dismisses it.
    /// </summary>
    [Fact]
    public async Task WhileAPasswordIsShown_NoOtherActionCanRun()
    {
        var viewModel = await LoadedAsync(Port());
        await viewModel.ResetCommand.ExecuteAsync(Row(viewModel, SofiaId));
        await viewModel.ConfirmCommand.ExecuteAsync(null);
        viewModel.NewUsername = "lucia";
        viewModel.NewDisplayName = "Lucía Díaz";

        Assert.False(viewModel.CreateCommand.CanExecute(null));
        Assert.False(viewModel.ResetCommand.CanExecute(Row(viewModel, SofiaId)));
        Assert.False(viewModel.DeactivateCommand.CanExecute(Row(viewModel, SofiaId)));
        Assert.False(viewModel.ReactivateCommand.CanExecute(Row(viewModel, MariaId)));

        viewModel.DismissCommand.Execute(null);

        Assert.True(viewModel.CreateCommand.CanExecute(null));
        Assert.True(viewModel.ResetCommand.CanExecute(Row(viewModel, SofiaId)));
    }

    /// <summary>A confirmation left open does not survive a create, so it cannot replace that create's password later.</summary>
    [Fact]
    public async Task APendingConfirmation_CannotReplaceAShownPassword()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        await viewModel.ResetCommand.ExecuteAsync(Row(viewModel, SofiaId));
        viewModel.NewUsername = "lucia";
        viewModel.NewDisplayName = "Lucía Díaz";

        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.Equal("lucia", viewModel.Revealed?.Username);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
        await viewModel.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("lucia", viewModel.Revealed?.Username);
        Assert.DoesNotContain(port.Calls, call => call.StartsWith("reset", StringComparison.Ordinal));
    }

    /// <summary>Spec Requirement 2 and Test 16: a successful create clears the form; a refusal keeps it and shows its own text.</summary>
    [Fact]
    public async Task Create_ClearsTheFormOnSuccess_AndKeepsItOnARefusal()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        viewModel.NewUsername = "maria";
        viewModel.NewDisplayName = "María Otra";
        port.OnCreate = (_, _) => UserAdminResult.Refused(UserAdminRefusal.UsernameTaken);

        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.Equal(UserAdminRefusal.UsernameTaken.OperatorText(), viewModel.RefusalMessage);
        Assert.Equal(("maria", "María Otra"), (viewModel.NewUsername, viewModel.NewDisplayName));
        Assert.Null(viewModel.Revealed);

        port.OnCreate = null;
        viewModel.NewUsername = "lucia";
        Assert.True(viewModel.CreateCommand.CanExecute(null));
        await viewModel.CreateCommand.ExecuteAsync(null);

        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal(("", ""), (viewModel.NewUsername, viewModel.NewDisplayName));
        Assert.Equal("lucia", viewModel.Revealed?.Username);
    }

    /// <summary>Spec Test 16: every refusal reaches the screen as its own text, never as the generic error.</summary>
    [Theory]
    [MemberData(nameof(AllRefusals))]
    public async Task EveryRefusal_IsShownAsItsOwnText(UserAdminRefusal refusal)
    {
        var port = Port();
        port.OnReset = _ => UserAdminResult.Refused(refusal);
        var viewModel = await LoadedAsync(port);

        await viewModel.ResetCommand.ExecuteAsync(Row(viewModel, SofiaId));
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(refusal.OperatorText(), viewModel.RefusalMessage);
        Assert.Null(viewModel.Runner.ErrorMessage);
        Assert.Null(viewModel.Revealed);
    }

    public static TheoryData<UserAdminRefusal> AllRefusals() => new(Enum.GetValues<UserAdminRefusal>());

    /// <summary>Spec Decision 18: the list is read again after every action, refused or not.</summary>
    [Fact]
    public async Task TheList_IsReadAgainAfterEveryAction()
    {
        var port = Port();
        var viewModel = await LoadedAsync(port);
        port.OnDeactivate = _ => UserAdminResult.Refused(UserAdminRefusal.CreatedByAnotherAdmin);
        port.Users[1] = port.Users[1] with { IsActive = false };

        await viewModel.DeactivateCommand.ExecuteAsync(Row(viewModel, SofiaId));
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(2, port.ListCalls);
        Assert.Equal("Desactivado", Row(viewModel, SofiaId).StateText);
        Assert.Equal(UserAdminRefusal.CreatedByAnotherAdmin.OperatorText(), viewModel.RefusalMessage);
    }

    /// <summary>Spec Test 13, view-model half: an unexpected failure shows the generic text and reveals no password.</summary>
    [Fact]
    public async Task AnUnexpectedFailure_IsTheGenericMessage_AndRevealsNothing()
    {
        var port = Port();
        port.OnReset = _ => throw new InvalidOperationException("ALTER ROLE sofia PASSWORD 'mar-pan-sol-12345'");
        var viewModel = await LoadedAsync(port);

        await viewModel.ResetCommand.ExecuteAsync(Row(viewModel, SofiaId));
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.Generic, viewModel.Runner.ErrorMessage);
        Assert.Null(viewModel.Revealed);
        Assert.Null(viewModel.Pending);
        Assert.False(viewModel.Runner.IsBusy);
    }

    /// <summary>The create command needs both fields.</summary>
    [Theory]
    [InlineData("", "Lucía Díaz")]
    [InlineData("lucia", "  ")]
    public async Task Create_NeedsBothFields(string username, string displayName)
    {
        var viewModel = await LoadedAsync(Port());
        viewModel.NewUsername = username;
        viewModel.NewDisplayName = displayName;

        Assert.False(viewModel.CreateCommand.CanExecute(null));
    }
}
