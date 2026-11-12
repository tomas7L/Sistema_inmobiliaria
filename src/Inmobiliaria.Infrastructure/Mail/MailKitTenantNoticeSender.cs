using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Notices;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Inmobiliaria.Infrastructure.Mail;

/// <summary>
/// Sends notices through Gmail with MailKit (notificaciones design §3.4, D7, D8). The server is fixed in code:
/// <c>smtp.gmail.com:587</c>, STARTTLS, a 30-second timeout. No exception ever leaves it: a failure becomes an outcome whose
/// text has the app password removed, because <see cref="SecretMask"/> alone does not cover a bare one (design Finding F4).
/// </summary>
public sealed class MailKitTenantNoticeSender : ITenantNoticeSender
{
    internal const string Host = "smtp.gmail.com";
    internal const int Port = 587;
    internal const int TimeoutMilliseconds = 30_000;

    /// <summary>What a refused login tells the operator, beside the server's own reason (design Finding F6).</summary>
    internal const string LoginRefused =
        "Gmail rechazó la dirección o la contraseña de aplicación. Si se cambió la contraseña de la cuenta de Gmail, todas sus "
        + "contraseñas de aplicación dejaron de valer: un Admin tiene que crear una nueva y cargarla.";

    private readonly Func<ISmtpTransport> _transport;

    public MailKitTenantNoticeSender()
        : this(() => new MailKitSmtpTransport())
    {
    }

    internal MailKitTenantNoticeSender(Func<ISmtpTransport> transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        _transport = transport;
    }

    public async Task<MailSessionOpening> OpenAsync(MailboxCredentials credentials, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var transport = _transport();
        try
        {
            await transport.ConnectAsync(Host, Port, ct);
            await transport.AuthenticateAsync(credentials.Address, credentials.AppPassword, ct);

            return new MailSessionOpening(new Session(transport, credentials), null);
        }
        catch (Exception exception)
        {
            // Closed on every failure, a cancelled login included, so no client is left open (Phase 2 verifier, advisory 2).
            await transport.DisposeAsync();
            if (exception is OperationCanceledException && ct.IsCancellationRequested)
            {
                throw;
            }

            var reason = Scrub(exception, credentials.AppPassword);
            return new MailSessionOpening(
                null,
                exception is AuthenticationException
                    ? $"{LoginRefused} ({reason})"
                    : $"No se pudo conectar con Gmail: {reason}");
        }
    }

    /// <summary>
    /// The exception's message with the password removed, with or without the spaces Google shows it with, and then the
    /// patterns <see cref="SecretMask"/> knows (design D7). The only text about a failure that leaves here.
    /// </summary>
    internal static string Scrub(Exception exception, string appPassword) => SecretMask.Mask(exception.Message, appPassword);

    /// <summary>
    /// Why one email failed, told apart by whether it may have gone out (gentle-ai review, R4-ambiguous-delivery). Gmail's own
    /// refusal and a send never started are definite: nothing went out. A connection lost or timed out while the email was
    /// travelling is not: Gmail may have delivered it, so the operator is told to look before resending.
    /// </summary>
    internal static string FailureOf(Exception exception, string appPassword)
    {
        var reason = Scrub(exception, appPassword);

        return exception switch
        {
            SmtpCommandException => $"Gmail no aceptó el correo: {reason}",
            ServiceNotConnectedException =>
                $"No se envió: se perdió la conexión con Gmail ({reason}). Se puede volver a enviar.",
            IOException or TimeoutException or OperationCanceledException or SmtpProtocolException =>
                $"No se sabe si el correo llegó: se cortó la conexión mientras se enviaba ({reason}). Revise la carpeta "
                + "Enviados de Gmail antes de volver a enviarlo.",
            _ => $"No se envió el correo: {reason}",
        };
    }

    private sealed class Session(ISmtpTransport transport, MailboxCredentials credentials) : IMailSession
    {
        public async Task<MailDelivery> SendAsync(OutgoingMail mail, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(mail);

            try
            {
                var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(credentials.Address));
                message.To.Add(MailboxAddress.Parse(mail.To));
                message.Subject = mail.Subject;
                message.Body = new TextPart("plain") { Text = mail.Body };

                await transport.SendAsync(message, ct);

                return new MailDelivery(true, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new MailDelivery(false, FailureOf(exception, credentials.AppPassword));
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await transport.DisconnectAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // The batch is over either way; a failed goodbye changes nothing that was sent or recorded.
            }

            await transport.DisposeAsync();
        }
    }
}

/// <summary>The few SMTP calls a send makes, so a test can fail them without a server (design §3.4).</summary>
internal interface ISmtpTransport : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, CancellationToken ct);

    Task AuthenticateAsync(string userName, string password, CancellationToken ct);

    Task SendAsync(MimeMessage message, CancellationToken ct);

    Task DisconnectAsync(CancellationToken ct);
}

/// <summary>MailKit's own client: STARTTLS on 587 and a 30-second timeout (design D8).</summary>
internal sealed class MailKitSmtpTransport : ISmtpTransport
{
    private readonly SmtpClient _client = new() { Timeout = MailKitTenantNoticeSender.TimeoutMilliseconds };

    public Task ConnectAsync(string host, int port, CancellationToken ct) =>
        _client.ConnectAsync(host, port, SecureSocketOptions.StartTls, ct);

    public Task AuthenticateAsync(string userName, string password, CancellationToken ct) =>
        _client.AuthenticateAsync(userName, password, ct);

    public Task SendAsync(MimeMessage message, CancellationToken ct) => _client.SendAsync(message, ct);

    public Task DisconnectAsync(CancellationToken ct) =>
        _client.IsConnected ? _client.DisconnectAsync(true, ct) : Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
