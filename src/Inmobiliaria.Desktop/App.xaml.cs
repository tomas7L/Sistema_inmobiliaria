using System.IO;
using System.Text.Json;
using System.Windows;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.GoLive;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Notices;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Access;
using Inmobiliaria.Infrastructure.IndexSources;
using Inmobiliaria.Presentation.CashDesk;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Desktop.Printing;
using Inmobiliaria.Presentation.GoLive;
using Inmobiliaria.Presentation.Indices;
using Inmobiliaria.Presentation.Login;
using Inmobiliaria.Presentation.Notices;
using Inmobiliaria.Presentation.Parties;
using Inmobiliaria.Presentation.Printing;
using Inmobiliaria.Presentation.Recibos;
using Inmobiliaria.Presentation.Voiding;
using Inmobiliaria.Presentation.Shell;
using Inmobiliaria.Presentation.Units;
using Inmobiliaria.Presentation.Users;
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
    private ServiceProvider? _sessionProvider;
    private ISessionDbContextFactory? _activeSessionFactory;
    private bool _shellShown;

    // One gate for the whole run, so the count of failed logins survives every pass of the bootstrap loop and every
    // Cerrar sesión (limite-de-intentos-de-login, design D5).
    private LoginGate? _loginGate;

    // async void is deliberate here, not an oversight: WPF's own lifecycle methods
    // (OnStartup/OnExit) return void and have no caller to await them — the well-established
    // pattern for a WPF composition root that must await anything during startup. Every
    // Npgsql/EF await inside this file runs on the UI thread's SynchronizationContext by
    // design, since the windows these awaits gate (ChangePasswordWindow, MainWindow) are
    // themselves only ever shown from this same thread.
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Every unexpected failure is kept on this PC for diagnosis (live smoke H.5); the screen still shows
        // only the fixed text. Installed first, so even a failure during login is recorded.
        OperationRunner.DefaultErrorLog = new FileErrorLog(ErrorLogFolder);

        // The last-resort backstop for an exception no screen operation caught (design D24): the same
        // fixed message OperationRunner shows, never the exception's text. Once the shell is up the window
        // stays open; before it, the application ends.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

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
        await EndSessionAsync();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Disposes the signed-in session: on exit, and on Cerrar sesión before the login window shows again (12.3d), so no
    /// container or connection of the old session outlives it. The session container goes before the factory it was
    /// built from.
    /// </summary>
    private async Task EndSessionAsync()
    {
        _sessionProvider?.Dispose();
        _sessionProvider = null;

        if (_activeSessionFactory is not null)
        {
            await _activeSessionFactory.DisposeAsync();
            _activeSessionFactory = null;
        }
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
        var loginGate = _loginGate ??= new LoginGate(TimeProvider.System, OperationRunner.DefaultErrorLog);

        while (true)
        {
            var authenticator = _serviceProvider!.GetRequiredService<IAuthenticator>();
            var loginViewModel = new LoginViewModel(authenticator, loginGate);
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

            // The second container, built only now that both gates are past (spec Requirement 1): it holds
            // the per-call ports and no database context (design D3).
            var recargo = LoadRecargoSettings();
            var sessionServices = new ServiceCollection();
            sessionServices.AddSessionServices(success.Factory, success.Session, recargo, LoadIndexSourceSettings());
            _sessionProvider = sessionServices.BuildServiceProvider();

            var settingsProblems = recargo.Problem is null ? Array.Empty<string>() : [recargo.Problem];
            var session = _sessionProvider.GetRequiredService<IUserSession>();

            // Built only for the role the shell offers it to (spec Requirement 2); the database still decides.
            var goLiveViewModel = session.Role == UserRole.Admin
                ? new GoLiveViewModel(_sessionProvider.GetRequiredService<IGoLiveSetup>())
                : null;
            var usersViewModel = session.Role == UserRole.Admin
                ? new UsersViewModel(_sessionProvider.GetRequiredService<IUserAdministration>(), new WpfClipboard())
                : null;

            // Every recibo display prints through the same seam, with the letterhead read once (Decision 37).
            var reader = _sessionProvider.GetRequiredService<IReciboReader>();
            var printing = BuildPrinting(reader);

            // The desk is registered only with valid recargo terms (design D16); without it the shell's entry
            // is disabled with the setting named, and no desk is built.
            var cashDeskViewModel = _sessionProvider.GetService<ICollectionDesk>() is { } collectionDesk
                ? new CashDeskViewModel(
                    collectionDesk,
                    _sessionProvider.GetRequiredService<IAccountDirectory>(),
                    _sessionProvider.GetRequiredService<IDueAdjustmentQuery>(),
                    reader,
                    _sessionProvider.GetRequiredService<IAgencyClock>(),
                    printing)
                : null;

            // Voiding is planned with the same recargo terms as the desk, so it exists only with the desk (D16).
            var voidingViewModel = _sessionProvider.GetService<ICollectionDesk>() is { } voidingDesk
                ? new VoidingViewModel(voidingDesk, reader, _sessionProvider.GetRequiredService<IAgencyClock>(), session.Role, printing)
                : null;

            MainWindow? mainWindow = null;
            var recentViewModel = new RecentCollectionsViewModel(reader, async collectionId =>
            {
                if (voidingViewModel is not null)
                {
                    mainWindow?.ShowScreen(NavigationTarget.Voiding);
                    await voidingViewModel.OpenAsync(collectionId);
                }
            }, canVoid: voidingViewModel is not null, printing: printing);

            // Avisos needs the desk's recargo terms, so it exists only with them (notificaciones spec Requirement 8). The
            // mailbox lives in this PC's Credential Manager, built here as the clipboard is, never in the session's
            // services (design D9).
            var noticesViewModel = _sessionProvider.GetService<ITenantNoticeDesk>() is { } noticeDesk
                ? new NoticesViewModel(noticeDesk, new CredentialManagerMailboxStore(), session.Role, LoadAgencyName())
                : null;

            // Personas is for both roles (gestion-de-alquileres spec Requirement 22).
            var partiesViewModel = new PartiesViewModel(_sessionProvider.GetRequiredService<IPartyRegistry>());

            // Propiedades too (gestion-de-alquileres spec Requirement 22).
            var unitsViewModel = new UnitsViewModel(_sessionProvider.GetRequiredService<IUnitRegistry>());

            // Índices too, with the official source behind "Traer valores" (spec Requirement 25).
            var indicesViewModel = new IndicesViewModel(
                _sessionProvider.GetRequiredService<IIndexRegistry>(),
                _sessionProvider.GetRequiredService<IIndexValueSource>(),
                _sessionProvider.GetRequiredService<IAgencyClock>(),
                _sessionProvider.GetRequiredService<IDueAdjustmentQuery>());

            // What each screen would lose if the session closed now, so Cerrar sesión asks first (12.3d, Tomás 2026-11-16):
            // the desk and voiding name a summary not confirmed; every other screen, an operation still running.
            IPendingWork?[] pendingWork =
            [
                cashDeskViewModel, voidingViewModel,
                new RunningWork(recentViewModel.Runner),
                goLiveViewModel is null ? null : new RunningWork(goLiveViewModel.Runner),
                usersViewModel is null ? null : new RunningWork(usersViewModel.Runner),
                noticesViewModel is null ? null : new RunningWork(noticesViewModel.Runner),
                new RunningWork(partiesViewModel.Runner),
                new RunningWork(unitsViewModel.Runner),
                new RunningWork(indicesViewModel.Runner),
            ];
            var shellViewModel = new ShellViewModel(session, settingsProblems, pendingWork.OfType<IPendingWork>().ToList());

            mainWindow = new MainWindow(
                shellViewModel, cashDeskViewModel, recentViewModel, voidingViewModel, goLiveViewModel, usersViewModel,
                noticesViewModel, partiesViewModel, unitsViewModel, indicesViewModel);
            MainWindow = mainWindow;
            // ShutdownMode is OnExplicitShutdown (App.xaml) precisely so that closing
            // LoginWindow or ChangePasswordWindow mid-bootstrap never ends the process before
            // the next window in the sequence is shown. Closing MainWindow ends it, unless the
            // window closed for Cerrar sesión: then the session is disposed and the login shows
            // again (12.3d, Mateo's guide: "Cerrar sesión vuelve a Inicio de sesión").
            var loggingOut = false;
            shellViewModel.LogOutRequested += (_, _) =>
            {
                loggingOut = true;
                mainWindow.Close();
            };
            mainWindow.Closed += async (_, _) =>
            {
                if (!loggingOut)
                {
                    Shutdown();
                    return;
                }

                _shellShown = false;
                MainWindow = null;
                await EndSessionAsync();
                await RunBootstrapAsync();
            };
            mainWindow.Show();
            _shellShown = true;
            return;
        }
    }

    /// <summary>
    /// <c>%LOCALAPPDATA%\SistemaInmobiliaria\logs</c>: writable by the Windows user without elevation, unlike
    /// the folder the application is installed in.
    /// </summary>
    private static string ErrorLogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SistemaInmobiliaria", "logs");

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            OperationRunner.DefaultErrorLog.Record("Unhandled", e.Exception);
        }
        catch (Exception)
        {
            // The backstop must still show its message.
        }

        MessageBox.Show(ErrorMessages.Generic, "Sistema Inmobiliaria", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;

        // Before the shell is shown (endpoint, login gates, recargo settings) nothing would ever call
        // Shutdown, and ShutdownMode is OnExplicitShutdown: handling the exception alone would leave a
        // windowless process running (S1 review, R3-dispatcher-backstop-zombie).
        if (!_shellShown)
        {
            Shutdown();
        }
    }

    /// <summary>
    /// The <c>Agency</c> section and the logo it names (Decision 37): read once, so a missing item or a logo
    /// file that does not exist stops every print before the dialog, naming it. Nothing is invented.
    /// </summary>
    private static ReciboPrinting BuildPrinting(IReciboReader reader)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var values = new Dictionary<string, string?>();
        if (document.RootElement.TryGetProperty(AgencyDetails.Section, out var agency) && agency.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in agency.EnumerateObject())
            {
                values[item.Name] = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : null;
            }
        }

        var read = AgencyDetails.Read(values);
        var logo = read.Details is { } details ? Path.Combine(AppContext.BaseDirectory, details.LogoFile) : string.Empty;
        if (read.Details is not null && !File.Exists(logo))
        {
            read = new AgencyDetailsRead(null, [$"{AgencyDetails.Section}:LogoFile (no existe el archivo {logo})"]);
        }

        return new ReciboPrinting(reader, new FlowDocumentReciboPrinter(read.Details is null ? "missing" : logo), read);
    }

    /// <summary>
    /// <c>Agency:Name</c>, which notices are signed with, or null when it is not set: the Avisos screen then says so and
    /// does not send (notificaciones). Read apart from the rest of the section, so a missing logo does not stop notices.
    /// </summary>
    private static string? LoadAgencyName()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            return document.RootElement.TryGetProperty(AgencyDetails.Section, out var agency)
                   && agency.ValueKind == JsonValueKind.Object
                   && agency.TryGetProperty("Name", out var name)
                   && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable file only disables sending, as a missing name does; it never stops the application
            // (gentle-ai review, R3-001).
            OperationRunner.DefaultErrorLog.Record("Agency:Name", exception);
            return null;
        }
    }

    /// <summary>
    /// The <c>Recargo</c> section of the same file: terms or one problem naming the key, never a default
    /// (design section 10). A problem does not stop the application; it closes the desk and voiding.
    /// </summary>
    private static RecargoSettings LoadRecargoSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return RecargoSettings.Read(document.RootElement);
    }

    /// <summary>
    /// The <c>IndexSources</c> section of the same file. A problem is not a settings problem of the shell's, which would close the
    /// desk: it only makes the source button report a failing source, and manual entry stays open.
    /// </summary>
    private static IndexSourceSettings LoadIndexSourceSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return IndexSourceSettings.Read(document.RootElement);
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
