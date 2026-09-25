using Npgsql;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// The connection coordinates every login composes against. None of these five values is a
/// secret (design Decision 2) — the password entered at login is the only credential, and it
/// never lives here or anywhere on disk.
/// </summary>
public sealed record ConnectionEndpoint(
    string Host,
    int Port,
    string Database,
    string ProjectRef,
    SslMode SslMode);
