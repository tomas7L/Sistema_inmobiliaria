namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// The password of one logged-in session, in memory only. The session's connection pool reads it every time
/// it opens a new physical connection, so a self password change must replace it: otherwise the pool keeps
/// authenticating with the old one, which the database now rejects (live smoke H.5, 2026-10-24).
/// </summary>
public sealed class SessionPassword
{
    private volatile string _value;

    public SessionPassword(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    public string Value => _value;

    public void Replace(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }
}
