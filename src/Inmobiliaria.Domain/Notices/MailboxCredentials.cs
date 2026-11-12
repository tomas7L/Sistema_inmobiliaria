namespace Inmobiliaria.Domain.Notices;

/// <summary>
/// The agency's Gmail address and its app password (notificaciones design §2.1, D9). The password lives only in Windows
/// Credential Manager and is handed to a send, never logged (spec Requirement 6): <see cref="ToString"/> prints the address only,
/// so an object dumped into a message or a log line never carries it.
/// </summary>
public sealed class MailboxCredentials
{
    public MailboxCredentials(string address, string appPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(appPassword);

        Address = address;
        AppPassword = appPassword;
    }

    /// <summary>The Gmail address notices are sent from. Not secret.</summary>
    public string Address { get; }

    /// <summary>The app password. Secret.</summary>
    public string AppPassword { get; }

    /// <summary>The address only, never the password.</summary>
    public override string ToString() => Address;
}
