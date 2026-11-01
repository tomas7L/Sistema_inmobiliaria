using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Users;

/// <summary>
/// The Usuarios screen (gestion-de-usuarios spec Requirements 1 to 8, design §5): every user, a form to create an
/// Empleado, and reset, deactivate and reactivate on Empleado rows, each confirmed first. A generated password is
/// shown until the Admin dismisses it and is never kept after that. Admin only, by the shell; the database refuses
/// an Empleado anyway. The list is read again after every action, refused or not.
/// </summary>
public sealed partial class UsersViewModel : ObservableObject
{
    private static readonly StringComparer ByName = StringComparer.Create(CultureInfo.GetCultureInfo("es-AR"), ignoreCase: true);

    private readonly IUserAdministration _users;
    private readonly IClipboard _clipboard;

    public UsersViewModel(IUserAdministration users, IClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(clipboard);

        _users = users;
        _clipboard = clipboard;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !Runner.IsBusy);
        CreateCommand = new AsyncRelayCommand(CreateAsync, CanCreate);
        ResetCommand = new AsyncRelayCommand<UserRow>(
            row => Ask(row, UserAction.Reset), row => CanAct() && row is { CanReset: true });
        DeactivateCommand = new AsyncRelayCommand<UserRow>(
            row => Ask(row, UserAction.Deactivate), row => CanAct() && row is { CanDeactivate: true });
        ReactivateCommand = new AsyncRelayCommand<UserRow>(
            row => Ask(row, UserAction.Reactivate), row => CanAct() && row is { CanReactivate: true });
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanAct() && Pending is not null);
        CancelCommand = new RelayCommand(() => Pending = null);
        CopyCommand = new RelayCommand(Copy, () => Revealed is not null);
        DismissCommand = new RelayCommand(Dismiss);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    public ObservableCollection<UserRow> Users { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand CreateCommand { get; }

    public IAsyncRelayCommand<UserRow> ResetCommand { get; }

    public IAsyncRelayCommand<UserRow> DeactivateCommand { get; }

    public IAsyncRelayCommand<UserRow> ReactivateCommand { get; }

    public IAsyncRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand CopyCommand { get; }

    public IRelayCommand DismissCommand { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _newUsername = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _newDisplayName = string.Empty;

    /// <summary>The action waiting for the Admin's confirmation, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private PendingUserAction? _pending;

    /// <summary>
    /// The generated password on screen, or null once dismissed (spec Requirement 6). While it is shown no other
    /// action can run, so a password already set in the database cannot be lost from the screen.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(CreateCommand), nameof(ResetCommand), nameof(DeactivateCommand), nameof(ReactivateCommand), nameof(ConfirmCommand))]
    private ProvisionalCredential? _revealed;

    /// <summary>Said when the clipboard could not be written; the password stays on screen to be copied by hand.</summary>
    [ObservableProperty]
    private string? _copyMessage;

    /// <summary>The last refusal's operator text, or null. Cleared when the next action starts.</summary>
    [ObservableProperty]
    private string? _refusalMessage;

    private async Task LoadAsync()
    {
        RefusalMessage = null;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var outcome = await Runner.RunAsync(() => _users.ListAsync());
        if (!outcome.Completed)
        {
            return;
        }

        Users.Clear();
        foreach (var listing in outcome.Value!.OrderByDescending(u => u.IsActive).ThenBy(u => u.DisplayName, ByName))
        {
            Users.Add(new UserRow(listing));
        }
    }

    private bool CanAct() => !Runner.IsBusy && Revealed is null;

    private bool CanCreate() =>
        CanAct() && !string.IsNullOrWhiteSpace(NewUsername) && !string.IsNullOrWhiteSpace(NewDisplayName);

    private void Copy()
    {
        CopyMessage = _clipboard.TrySetText(Revealed!.Password)
            ? null
            : "No se pudo copiar. Seleccione la contraseña y cópiela a mano.";
    }

    private void Dismiss()
    {
        if (Revealed is { } credential)
        {
            _clipboard.ClearIfHolds(credential.Password);
        }

        CopyMessage = null;
        Revealed = null;
    }

    private async Task CreateAsync()
    {
        // A confirmation left open is dropped: confirming it later would replace this create's password on screen.
        Pending = null;
        var result = await RunAsync(() => _users.CreateEmpleadoAsync(NewUsername, NewDisplayName));
        if (result is { Succeeded: true })
        {
            NewUsername = string.Empty;
            NewDisplayName = string.Empty;
        }
    }

    private Task Ask(UserRow? row, UserAction action)
    {
        if (row is not null)
        {
            RefusalMessage = null;
            Pending = new PendingUserAction(action, row.Id, ConfirmationText(action, row.DisplayName));
        }

        return Task.CompletedTask;
    }

    private async Task ConfirmAsync()
    {
        if (Pending is not { } pending)
        {
            return;
        }

        Pending = null;
        await RunAsync(() => pending.Action switch
        {
            UserAction.Reset => _users.ResetPasswordAsync(pending.UserId),
            UserAction.Deactivate => _users.DeactivateAsync(pending.UserId),
            UserAction.Reactivate => _users.ReactivateAsync(pending.UserId),
            _ => throw new ArgumentOutOfRangeException(nameof(pending), pending.Action, "An action without a port call."),
        });
    }

    /// <summary>
    /// Runs one action, then reads the list again, refused or not (spec Decision 18), and shows its password or
    /// refusal. A failure leaves the generic message on screen and the list as it was, as on the go-live screen.
    /// </summary>
    private async Task<UserAdminResult?> RunAsync(Func<Task<UserAdminResult>> action)
    {
        RefusalMessage = null;

        var outcome = await Runner.RunAsync(action);
        if (!outcome.Completed)
        {
            return null;
        }

        var result = outcome.Value!;
        await ReloadAsync();
        RefusalMessage = result.Refusal?.OperatorText();
        Revealed = result.Credential;
        return result;
    }

    private static string ConfirmationText(UserAction action, string name) => action switch
    {
        UserAction.Reset =>
            $"¿Reiniciar la contraseña de {name}? Va a recibir una contraseña nueva y tendrá que cambiarla al entrar.",
        UserAction.Deactivate =>
            $"¿Desactivar a {name}? No va a poder volver a entrar. Si ahora tiene el sistema abierto, sigue trabajando hasta que lo cierre.",
        UserAction.Reactivate =>
            $"¿Reactivar a {name}? Va a poder entrar de nuevo con una contraseña nueva.",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "An action without confirmation text."),
    };

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            CreateCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();
            DeactivateCommand.NotifyCanExecuteChanged();
            ReactivateCommand.NotifyCanExecuteChanged();
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }
}

public enum UserAction
{
    Reset,
    Deactivate,
    Reactivate,
}

/// <summary>An action the Admin asked for and has not confirmed yet; <see cref="Text"/> names the person.</summary>
public sealed record PendingUserAction(UserAction Action, Guid UserId, string Text);

/// <summary>One user on the Usuarios screen. Only an Empleado row has actions (spec Requirement 7).</summary>
public sealed class UserRow
{
    public UserRow(UserListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        Id = listing.Id;
        DisplayName = listing.DisplayName;
        Username = listing.Username;
        RoleText = listing.Role switch
        {
            UserRole.Admin => "Administrador",
            UserRole.Empleado => "Empleado",
            _ => string.Empty,
        };
        StateText = listing.IsActive ? "Activo" : "Desactivado";
        PendingText = listing.MustChangePassword ? "Debe cambiar la contraseña" : string.Empty;

        var isEmpleado = listing.Role == UserRole.Empleado;
        CanReset = isEmpleado && listing.IsActive;
        CanDeactivate = isEmpleado && listing.IsActive;
        CanReactivate = isEmpleado && !listing.IsActive;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public string Username { get; }

    public string RoleText { get; }

    public string StateText { get; }

    public string PendingText { get; }

    public bool CanReset { get; }

    public bool CanDeactivate { get; }

    public bool CanReactivate { get; }
}
