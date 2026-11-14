namespace Inmobiliaria.Presentation.Login;

/// <summary>
/// How one login attempt ended, in the terms <see cref="LoginGate"/> counts by. Presentation may not see the
/// authenticator's own result, so the login window translates it into this (limite-de-intentos-de-login, design D3).
/// </summary>
public abstract record LoginOutcome
{
    private LoginOutcome()
    {
    }

    /// <summary>The database accepted the login and the session can start.</summary>
    public sealed record Accepted : LoginOutcome;

    /// <summary>The database accepted the login, but the account cannot use the application.</summary>
    public sealed record NotProvisioned : LoginOutcome;

    /// <summary>The database refused the username and password: one failed login.</summary>
    public sealed record Rejected : LoginOutcome;

    /// <summary>The pooler refused every new connection after too many failed logins.</summary>
    public sealed record Blocked(Exception Cause) : LoginOutcome;

    /// <summary>The database never judged the login: no network, a dropped connection, a broken setup.</summary>
    public sealed record NoVerdict(Exception Cause) : LoginOutcome;
}
