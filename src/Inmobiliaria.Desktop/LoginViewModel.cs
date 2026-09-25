using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Desktop;

/// <summary>
/// Backs <c>LoginWindow</c>. Depends only on <see cref="IAuthenticator"/> (design Decision 10's
/// table) — it knows nothing about Npgsql, connection strings, or <c>pg_has_role</c>; login IS
/// the connection attempt (spec "Login Is the Connection Attempt"), so there is nothing here to
/// verify a password against beyond calling <see cref="IAuthenticator.AuthenticateAsync"/> and
/// reacting to what PostgreSQL decided.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthenticator _authenticator;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>
    /// Raised exactly once, the moment <see cref="AuthenticationResult.Success"/> is reached.
    /// <c>LoginWindow</c> reacts by setting its own <c>DialogResult = true</c>; the caller reads
    /// <see cref="SuccessResult"/> afterward from <c>ShowDialog()</c>'s return.
    /// </summary>
    public event EventHandler? LoginSucceeded;

    /// <summary>
    /// Set only on <see cref="AuthenticationResult.Success"/>. This is the ONLY way the
    /// composition root (<c>App.xaml.cs</c>) ever obtains a session or a database factory —
    /// design Decision 1's object graph, not a separately resolved dependency.
    /// </summary>
    public AuthenticationResult.Success? SuccessResult { get; private set; }

    public LoginViewModel(IAuthenticator authenticator)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        _authenticator = authenticator;
    }

    private bool CanLogin() =>
        !IsBusy && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _authenticator.AuthenticateAsync(Username, Password);

            switch (result)
            {
                case AuthenticationResult.Success success:
                    SuccessResult = success;
                    LoginSucceeded?.Invoke(this, EventArgs.Empty);
                    break;

                case AuthenticationResult.Rejected:
                    // Wrong password and unknown username are deliberately indistinguishable
                    // (spec "Wrong Password Is Rejected Without Revealing Whether The Username
                    // Exists") — one generic message covers both.
                    ErrorMessage = "Usuario o contraseña incorrectos.";
                    break;

                case AuthenticationResult.NotProvisioned:
                    // Deliberately a DIFFERENT message (design Decision 10) — this is not a
                    // credential problem, and the generic message above would send the user
                    // hunting for a typo in a password that was correct.
                    ErrorMessage = "Esta cuenta no puede iniciar sesión. Consulte al administrador.";
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnUsernameChanged(string value) => LoginCommand.NotifyCanExecuteChanged();

    partial void OnPasswordChanged(string value) => LoginCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => LoginCommand.NotifyCanExecuteChanged();
}
