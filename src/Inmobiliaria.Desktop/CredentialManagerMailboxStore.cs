using Inmobiliaria.Domain.Notices;
using Meziantou.Framework.Win32;

namespace Inmobiliaria.Desktop;

/// <summary>
/// The mailbox this PC sends notices from, kept in Windows Credential Manager (notificaciones design D9, spec Requirement 6):
/// one generic credential named <see cref="Name"/>, its user name the Gmail address and its secret the app password,
/// persisted for this Windows user on this PC only. Nothing else holds the password: no file, setting, environment variable
/// or database. Kept to the package's read and write calls, because <c>Desktop</c> has no CI test; checked by hand (design §5).
/// </summary>
public sealed class CredentialManagerMailboxStore : IMailboxCredentialStore
{
    /// <summary>The credential's name, as Windows' Credential Manager lists it under "Windows Credentials".</summary>
    public const string Name = "SistemaInmobiliaria/Gmail";

    public MailboxCredentials? Read()
    {
        var credential = CredentialManager.ReadCredential(Name);
        // A credential edited by hand in Windows to an empty field reads as not set up, never as an exception.
        return credential is not null
               && !string.IsNullOrWhiteSpace(credential.UserName)
               && !string.IsNullOrWhiteSpace(credential.Password)
            ? new MailboxCredentials(credential.UserName, credential.Password)
            : null;
    }

    public void Save(MailboxCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        // LocalMachine: survives a logoff and stays on this PC; it does not roam with the Windows profile.
        CredentialManager.WriteCredential(Name, credentials.Address, credentials.AppPassword, CredentialPersistence.LocalMachine);
    }
}
