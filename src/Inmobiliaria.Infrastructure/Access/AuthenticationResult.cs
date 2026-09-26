using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// The outcome of one login attempt (design Decision 1). <see cref="Success"/> is the ONLY
/// path that ever produces an <see cref="ISessionDbContextFactory"/> — there is no other
/// constructor, factory method, or DI registration anywhere in the process that yields one, so
/// "no database access before authentication" is a property of the object graph rather than a
/// check anybody could forget to write.
/// </summary>
public abstract record AuthenticationResult
{
    private AuthenticationResult()
    {
    }

    public sealed record Success(IUserSession Session, ISessionDbContextFactory Factory) : AuthenticationResult;

    /// <summary>
    /// Wrong password OR unknown username — deliberately indistinguishable (spec "Wrong
    /// Password Is Rejected Without Revealing Whether The Username Exists"). Carries no reason
    /// at all, precisely so the caller has nothing more specific to leak to the UI.
    /// </summary>
    public sealed record Rejected : AuthenticationResult;

    /// <summary>
    /// PostgreSQL authenticated the connection, but the account is not a usable application
    /// account: no <c>app_users</c> row, <c>IsActive = false</c>, or a member of neither
    /// application role. Deliberately a DIFFERENT message from <see cref="Rejected"/> — this is
    /// not a credential problem, and a generic message would send the user hunting for a typo
    /// in a password that was correct (design Decision 10).
    /// </summary>
    public sealed record NotProvisioned(string Reason) : AuthenticationResult;
}
