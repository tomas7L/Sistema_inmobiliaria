using System.IO;
using System.Text.Json;
using System.Windows;
using Inmobiliaria.Infrastructure.Access;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inmobiliaria.Desktop;

/// <summary>
/// The composition root (design Decision 7 / Decision 10). Everything that speaks EF Core or
/// Npgsql outside <see cref="Inmobiliaria.Infrastructure.Access"/> itself is confined to this
/// one file — no other Desktop type references <c>Npgsql</c> or
/// <c>Microsoft.EntityFrameworkCore</c> (task 6.9's guardrail).
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private ISessionDbContextFactory? _activeSessionFactory;

    // async void is deliberate here, not an oversight: WPF's own lifecycle methods
    // (OnStartup/OnExit) return void and have no caller to await them — the well-established
    // pattern for a WPF composition root that must await anything during startup. Every
    // Npgsql/EF await inside this file runs on the UI thread's SynchronizationContext by
    // design, since the windows these awaits gate (ChangePasswordWindow, MainWindow) are
    // themselves only ever shown from this same thread.
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var endpoint = LoadConnectionEndpoint();

        var services = new ServiceCollection();
        services.AddPreLoginServices(endpoint);
        // The pre-login container holds NO DbContext, NO NpgsqlDataSource, NO
        // ISessionDbContextFactory — spec "No Database Access Before Authentication" is a
        // property of this object graph, proven by CompositionGuardTests, not merely stated
        // here (design Decision 7).
        _serviceProvider = services.BuildServiceProvider();

        await RunBootstrapAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_activeSessionFactory is not null)
        {
            await _activeSessionFactory.DisposeAsync();
            _activeSessionFactory = null;
        }

        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// The exact sequence design Decision 10 diagrams: show <c>LoginWindow</c>; on a successful
    /// login whose <see cref="Domain.Access.IUserSession.MustChangePassword"/> is pending, show
    /// <c>ChangePasswordWindow</c> (forced) BEFORE constructing anything else; a cancelled
    /// forced change disposes the session and returns to <c>LoginWindow</c> — it never falls
    /// through to <c>MainWindow</c>. Driven through <see cref="LoginBootstrap"/>'s stages
    /// (task 6.7) rather than a second copy of the same rule.
    /// </summary>
    private async Task RunBootstrapAsync()
    {
        while (true)
        {
            var authenticator = _serviceProvider!.GetRequiredService<IAuthenticator>();
            var loginViewModel = new LoginViewModel(authenticator);
            var loginWindow = new LoginWindow(loginViewModel);

            var loginDialogResult = loginWindow.ShowDialog();

            if (loginDialogResult != true || loginViewModel.SuccessResult is not { } success)
            {
                Shutdown();
                return;
            }

            var stage = LoginBootstrap.AfterAuthentication(success);

            if (stage == BootstrapStage.AwaitingForcedPasswordChange)
            {
                var passwordService = new PostgresPasswordService(success.Factory, success.Session);
                var changeViewModel = new ChangePasswordViewModel(passwordService, success.Session, isForced: true);
                var changeWindow = new ChangePasswordWindow(changeViewModel);

                var changeDialogResult = changeWindow.ShowDialog();
                stage = LoginBootstrap.AfterForcedPasswordChange(succeeded: changeDialogResult == true);

                if (stage == BootstrapStage.Aborted)
                {
                    // Closed without success — Cancel, or the window chrome alike (design
                    // Decision 10: "closed without success ⇒ dispose the session and return to
                    // LoginWindow"). MainWindow is never constructed on this path.
                    await success.Factory.DisposeAsync();
                    continue;
                }
            }

            // Reached only past both gates (design Decision 10's diagram).
            _activeSessionFactory = success.Factory;

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            // ShutdownMode is OnExplicitShutdown (App.xaml) precisely so that closing
            // LoginWindow or ChangePasswordWindow mid-bootstrap never ends the process before
            // the next window in the sequence is shown; MainWindow closing is the one event
            // that actually should end it.
            mainWindow.Closed += (_, _) => Shutdown();
            mainWindow.Show();
            return;
        }
    }

    /// <summary>
    /// Host/Port/Database/ProjectRef/SslMode come from <c>appsettings.json</c> (design Decision
    /// 2) — none of the five is a secret; the password entered at login is the only credential,
    /// and it never lives on disk (spec "No Database Credential Stored on Disk"). Read with
    /// <see cref="JsonDocument"/> directly rather than <c>Microsoft.Extensions.Configuration</c>,
    /// which this change does not add — task 6.1 names only <c>CommunityToolkit.Mvvm</c> and
    /// <c>Microsoft.Extensions.DependencyInjection</c> as the packages this slice needs.
    /// </summary>
    private static ConnectionEndpoint LoadConnectionEndpoint()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var supabase = document.RootElement.GetProperty("Supabase");

        return new ConnectionEndpoint(
            Host: supabase.GetProperty("Host").GetString()!,
            Port: supabase.GetProperty("Port").GetInt32(),
            Database: supabase.GetProperty("Database").GetString()!,
            ProjectRef: supabase.GetProperty("ProjectRef").GetString()!,
            SslMode: Enum.Parse<SslMode>(supabase.GetProperty("SslMode").GetString()!, ignoreCase: true));
    }
}
