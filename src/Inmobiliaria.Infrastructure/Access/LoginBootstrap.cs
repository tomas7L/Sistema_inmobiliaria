namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// The exact sequence a stage MUST pass through before <c>MainWindow</c> (or anything else) may
/// be constructed (design Decision 10's diagram, expressed as a pure state machine). This type
/// carries no WPF reference, so it — and the rule it encodes — is provable from
/// <c>Inmobiliaria.Infrastructure.Tests</c>, which the Linux <c>core</c> job runs on every PR;
/// <c>Inmobiliaria.Desktop</c> itself is excluded from <c>Inmobiliaria.Core.slnf</c> and cannot
/// gate anything there (spec test 15's construction-order half; users-and-roles task 6.7).
/// <c>App.xaml.cs</c> drives its real windows through these same stages rather than duplicating
/// the decision.
/// </summary>
public enum BootstrapStage
{
    /// <summary>No session yet. <c>LoginWindow</c> is the only window that may be showing.</summary>
    AwaitingLogin,

    /// <summary>
    /// A session exists, but <see cref="Domain.Access.IUserSession.MustChangePassword"/> is
    /// still pending. <c>ChangePasswordWindow</c> (forced) is the only window that may be
    /// showing — not <c>MainWindow</c>, not anything else.
    /// </summary>
    AwaitingForcedPasswordChange,

    /// <summary>Every gate has passed. <c>MainWindow</c> may now be constructed.</summary>
    ReadyForMainWindow,

    /// <summary>
    /// The forced change was cancelled instead of completed. The session MUST be disposed and
    /// the flow MUST return to <c>LoginWindow</c> — it MUST NOT fall through to
    /// <see cref="ReadyForMainWindow"/>.
    /// </summary>
    Aborted
}

/// <summary>See <see cref="BootstrapStage"/> for what each stage means and why it exists.</summary>
public static class LoginBootstrap
{
    /// <summary>
    /// The stage immediately after a login attempt. A <see cref="AuthenticationResult.Rejected"/>
    /// or <see cref="AuthenticationResult.NotProvisioned"/> result stays at
    /// <see cref="BootstrapStage.AwaitingLogin"/> — <c>LoginWindow</c> shows its error message
    /// and tries again; nothing beyond it is ever reachable from a failed attempt.
    /// </summary>
    public static BootstrapStage AfterAuthentication(AuthenticationResult result) =>
        result switch
        {
            AuthenticationResult.Success success when success.Session.MustChangePassword
                => BootstrapStage.AwaitingForcedPasswordChange,
            AuthenticationResult.Success
                => BootstrapStage.ReadyForMainWindow,
            _ => BootstrapStage.AwaitingLogin
        };

    /// <summary>
    /// The stage immediately after a forced <c>ChangePasswordWindow</c> closes.
    /// <paramref name="succeeded"/> is <see langword="false"/> for every non-success close —
    /// clicking Cancel or the window chrome alike — and both MUST land on
    /// <see cref="BootstrapStage.Aborted"/>, never <see cref="BootstrapStage.ReadyForMainWindow"/>.
    /// </summary>
    public static BootstrapStage AfterForcedPasswordChange(bool succeeded) =>
        succeeded ? BootstrapStage.ReadyForMainWindow : BootstrapStage.Aborted;
}
