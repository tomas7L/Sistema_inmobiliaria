using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Infrastructure.Access;
using Inmobiliaria.Presentation.Login;

namespace Inmobiliaria.Desktop;

/// <summary>
/// Backs <c>LoginWindow</c>. Depends only on <see cref="IAuthenticator"/> (design Decision 10's
/// table) — it knows nothing about Npgsql, connection strings, or <c>pg_has_role</c>; login IS
/// the connection attempt (spec "Login Is the Connection Attempt"), so there is nothing here to
/// verify a password against beyond calling <see cref="IAuthenticator.AuthenticateAsync"/> and
/// reacting to what PostgreSQL decided.
/// <para>
/// What counts as a failure, when the window waits, which message shows and what is logged are
/// <see cref="LoginGate"/>'s, which CI tests (limite-de-intentos-de-login, design §4). This class
/// only translates the authenticator's answer for it and shows what it says.
/// </para>
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private const string LoginButtonText = "Ingresar";

    private readonly IAuthenticator _authenticator;
    private readonly LoginGate _gate;
    private readonly DispatcherTimer _waitTimer;

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

    /// <summary>The title above the message during a wait, or null.</summary>
    public string? WaitTitle => _gate.WaitTitle;

    /// <summary>"Ingresar", or the time left during a wait, such as "Espere 2:30".</summary>
    public string ButtonText => _gate.IsWaiting ? LoginTexts.Countdown(_gate.TimeLeft) : LoginButtonText;

    /// <param name="authenticator">Attempts the login.</param>
    /// <param name="gate">The one gate for the whole run of the application, so the count survives this window.</param>
    public LoginViewModel(IAuthenticator authenticator, LoginGate gate)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(gate);
        _authenticator = authenticator;
        _gate = gate;
        _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _waitTimer.Tick += (_, _) => ShowGate();

        // Shows whatever the gate holds when the window opens. Today a new window follows a successful login, which
        // clears the count, so this shows nothing; it keeps the window right if the loop ever opens one mid-wait.
        ShowGate();
    }

    private bool CanLogin() =>
        !IsBusy && !_gate.IsWaiting && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        AuthenticationResult.Success? success = null;
        try
        {
            await _gate.TryLogInAsync(async () =>
            {
                var result = await _authenticator.AuthenticateAsync(Username, Password);
                success = result as AuthenticationResult.Success;
                return Translate(result);
            });
        }
        finally
        {
            IsBusy = false;
            ShowGate();
        }

        if (success is not null)
        {
            SuccessResult = success;
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The authenticator's answer in the gate's terms (limite-de-intentos-de-login, design D3).</summary>
    private static LoginOutcome Translate(AuthenticationResult result) => result switch
    {
        AuthenticationResult.Success => new LoginOutcome.Accepted(),
        AuthenticationResult.NotProvisioned => new LoginOutcome.NotProvisioned(),
        AuthenticationResult.Rejected => new LoginOutcome.Rejected(),
        AuthenticationResult.Blocked blocked => new LoginOutcome.Blocked(blocked.Cause),
        AuthenticationResult.Unavailable unavailable => new LoginOutcome.NoVerdict(unavailable.Cause),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "An authentication result the login does not know."),
    };

    /// <summary>Shows what the gate says, and ticks once a second only while a wait is on.</summary>
    private void ShowGate()
    {
        ErrorMessage = _gate.Message;
        OnPropertyChanged(nameof(WaitTitle));
        OnPropertyChanged(nameof(ButtonText));
        LoginCommand.NotifyCanExecuteChanged();

        if (_gate.IsWaiting)
        {
            _waitTimer.Start();
        }
        else
        {
            _waitTimer.Stop();
        }
    }

    partial void OnUsernameChanged(string value) => LoginCommand.NotifyCanExecuteChanged();

    partial void OnPasswordChanged(string value) => LoginCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => LoginCommand.NotifyCanExecuteChanged();
}
