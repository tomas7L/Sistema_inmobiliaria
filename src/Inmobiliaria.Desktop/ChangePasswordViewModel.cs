using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Desktop;

/// <summary>
/// Backs <c>ChangePasswordWindow</c>, in both of its two uses (design Decision 10's table):
/// forced, right after a login whose <see cref="IUserSession.MustChangePassword"/> is pending,
/// and voluntary, from anywhere already past login. <see cref="IsForced"/> is the only thing
/// that differs between the two — it toggles whether a Cancel option exists at all. Depends
/// only on <see cref="IPasswordService"/> and <see cref="IUserSession"/>.
/// </summary>
public sealed partial class ChangePasswordViewModel : ObservableObject
{
    private readonly IPasswordService _passwordService;
    private readonly IUserSession _session;

    [ObservableProperty]
    private string currentPassword = string.Empty;

    [ObservableProperty]
    private string newPassword = string.Empty;

    [ObservableProperty]
    private string confirmNewPassword = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>
    /// When <see langword="true"/> (the bootstrap's forced use), a Cancel option exists — but
    /// exercising it does NOT skip the requirement. It raises <see cref="Cancelled"/>, and
    /// design Decision 10 requires the caller to dispose the session and return to
    /// <c>LoginWindow</c>, never to fall through to <c>MainWindow</c> or anything else. When
    /// <see langword="false"/> (a voluntary change, already past login), there is nothing a
    /// Cancel needs to protect against, so no Cancel option is offered.
    /// </summary>
    public bool IsForced { get; }

    /// <summary>The signed-in user this change applies to — always the caller's own session (self-service only).</summary>
    public string DisplayName => _session.DisplayName;

    /// <summary>Raised exactly once, the moment the password change succeeds.</summary>
    public event EventHandler? PasswordChangeSucceeded;

    /// <summary>
    /// Raised only when <see cref="IsForced"/> is <see langword="true"/> and the user cancels
    /// instead of completing the change.
    /// </summary>
    public event EventHandler? Cancelled;

    public ChangePasswordViewModel(IPasswordService passwordService, IUserSession session, bool isForced)
    {
        ArgumentNullException.ThrowIfNull(passwordService);
        ArgumentNullException.ThrowIfNull(session);

        _passwordService = passwordService;
        _session = session;
        IsForced = isForced;
    }

    private bool CanChange() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(CurrentPassword) &&
        !string.IsNullOrWhiteSpace(NewPassword) &&
        !string.IsNullOrWhiteSpace(ConfirmNewPassword);

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ChangeAsync()
    {
        ErrorMessage = null;

        if (!string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "Las contraseñas nuevas no coinciden.";
            return;
        }

        IsBusy = true;
        try
        {
            await _passwordService.ChangeOwnPasswordAsync(CurrentPassword, NewPassword);
            PasswordChangeSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException)
        {
            // Spec "Re-Entering The Provisional Password Is Rejected" — the same value was
            // submitted as both current and new. The requirement stays pending; this is not a
            // security message, only credential hygiene (spec Decision 14), so it says exactly
            // that and nothing more.
            ErrorMessage = "La nueva contraseña debe ser distinta de la actual.";
        }
        catch (Exception)
        {
            // Every other failure (e.g. a wrong current password rejected by PostgreSQL itself)
            // gets one generic message — this dialog draws no distinction the way login does,
            // because nothing here needs to hide whether an account exists.
            ErrorMessage = "No se pudo cambiar la contraseña. Verifique la contraseña actual.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCancel() => IsForced;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCurrentPasswordChanged(string value) => ChangeCommand.NotifyCanExecuteChanged();

    partial void OnNewPasswordChanged(string value) => ChangeCommand.NotifyCanExecuteChanged();

    partial void OnConfirmNewPasswordChanged(string value) => ChangeCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => ChangeCommand.NotifyCanExecuteChanged();
}
