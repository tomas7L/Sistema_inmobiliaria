namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// Login IS the connection attempt (spec "Login Is the Connection Attempt"). There is nothing
/// here to verify a password against — none is stored — only PostgreSQL's own handshake.
/// </summary>
public interface IAuthenticator
{
    Task<AuthenticationResult> AuthenticateAsync(
        string username, string password, CancellationToken ct = default);
}
