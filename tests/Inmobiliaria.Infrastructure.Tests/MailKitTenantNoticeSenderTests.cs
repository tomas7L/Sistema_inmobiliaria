using Inmobiliaria.Domain.Notices;
using Inmobiliaria.Infrastructure.Mail;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// notificaciones spec Test 13 (Requirement 6), design D5, D7 and D8: the MailKit adapter, through its SMTP seam, with no
/// server. A failure is an outcome, never an exception, and its text never carries the app password, written with or without
/// the spaces Google shows it with, even when the server's own message quotes it.
/// </summary>
public class MailKitTenantNoticeSenderTests
{
    private const string Password = "abcd efgh ijkl mnop";

    private static readonly MailboxCredentials Mailbox = new("dellago.avisos@example.com", Password);

    private static readonly OutgoingMail Mail = new("maria.perez@example.com", "Asunto", "Cuerpo");

    private static void AssertNoPassword(string? text)
    {
        Assert.NotNull(text);
        Assert.DoesNotContain(Password, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abcdefghijklmnop", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abcd", text, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Test 13 --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ARejectedAppPassword_IsAnOutcome_WhoseTextNeverCarriesThePassword()
    {
        var transport = new FakeTransport
        {
            OnAuthenticate = (_, password) => throw new AuthenticationException(
                $"535 5.7.8 Username and Password not accepted for {password} (also {password.Replace(" ", "")}, "
                + $"Password={password})"),
        };

        var opening = await new MailKitTenantNoticeSender(() => transport).OpenAsync(Mailbox);

        Assert.Null(opening.Session);
        AssertNoPassword(opening.FailureReason);
        Assert.StartsWith(MailKitTenantNoticeSender.LoginRefused, opening.FailureReason);
        Assert.Contains("535 5.7.8", opening.FailureReason);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task ASendThatFails_QuotingThePassword_IsAnOutcome_WhoseTextNeverCarriesIt()
    {
        var transport = new FakeTransport
        {
            OnSend = _ => throw new SmtpCommandFailedForTest($"550 rejected, session key ABCD EFGH IJKL MNOP / {Password}"),
        };
        var opening = await new MailKitTenantNoticeSender(() => transport).OpenAsync(Mailbox);
        await using var session = opening.Session!;

        var delivery = await session.SendAsync(Mail);

        Assert.False(delivery.Accepted);
        AssertNoPassword(delivery.FailureReason);
        Assert.Contains("550 rejected", delivery.FailureReason);
    }

    public static TheoryData<Exception, string> SendFailures => new()
    {
        { new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 no such user"),
            "Gmail no aceptó el correo: 550 no such user" },
        { new ServiceNotConnectedException("The SmtpClient is not connected."),
            "No se envió: se perdió la conexión con Gmail (The SmtpClient is not connected.). Se puede volver a enviar." },
        { new IOException("Connection reset by peer"),
            "No se sabe si el correo llegó: se cortó la conexión mientras se enviaba (Connection reset by peer). Revise la "
            + "carpeta Enviados de Gmail antes de volver a enviarlo." },
        { new TimeoutException("The operation has timed out."),
            "No se sabe si el correo llegó: se cortó la conexión mientras se enviaba (The operation has timed out.). Revise "
            + "la carpeta Enviados de Gmail antes de volver a enviarlo." },
        { new InvalidOperationException("MIME encoding failed"), "No se envió el correo: MIME encoding failed" },
        { new SmtpProtocolException("Unexpected end of stream"),
            "No se sabe si el correo llegó: se cortó la conexión mientras se enviaba (Unexpected end of stream). Revise la "
            + "carpeta Enviados de Gmail antes de volver a enviarlo." },
    };

    /// <summary>
    /// A refusal or a send never started says nothing went out; a connection lost mid-send says the email may have arrived, so
    /// the operator looks before resending (gentle-ai review, R4-ambiguous-delivery and R4-session-no-reconnect).
    /// </summary>
    [Theory]
    [MemberData(nameof(SendFailures))]
    public async Task ASendFailure_SaysWhetherTheEmailMayHaveGoneOut(Exception failure, string reason)
    {
        var transport = new FakeTransport { OnSend = _ => throw failure };
        var opening = await new MailKitTenantNoticeSender(() => transport).OpenAsync(Mailbox);
        await using var session = opening.Session!;

        var delivery = await session.SendAsync(Mail);

        Assert.False(delivery.Accepted);
        Assert.Equal(reason, delivery.FailureReason);
    }

    [Fact]
    public async Task AnUnreachableServer_IsAnOutcome_NamingTheConnection()
    {
        var transport = new FakeTransport { OnConnect = () => throw new IOException("No route to host") };

        var opening = await new MailKitTenantNoticeSender(() => transport).OpenAsync(Mailbox);

        Assert.Null(opening.Session);
        Assert.Equal("No se pudo conectar con Gmail: No route to host", opening.FailureReason);
        Assert.Equal(0, transport.Authentications);
    }

    [Fact]
    public async Task ACancelledLogin_IsRethrown_AndClosesTheClient()
    {
        using var press = new CancellationTokenSource();
        await press.CancelAsync();
        var transport = new FakeTransport { OnConnect = () => throw new OperationCanceledException(press.Token) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MailKitTenantNoticeSender(() => transport).OpenAsync(Mailbox, press.Token));

        Assert.True(transport.Disposed);
    }

    // ---- Design D5 and D8: log in first, to Gmail, then one message per notice ------------------------------------------

    [Fact]
    public async Task OpeningConnectsToGmailOn587_ThenLogsIn_AndASendCarriesTheComposedEmail()
    {
        var transport = new FakeTransport();
        var opening = await new MailKitTenantNoticeSender(() => transport).OpenAsync(Mailbox);

        Assert.Null(opening.FailureReason);
        Assert.Equal(["connect smtp.gmail.com:587", "auth dellago.avisos@example.com"], transport.Calls);

        await using (var session = opening.Session!)
        {
            var delivery = await session.SendAsync(Mail);
            Assert.True(delivery.Accepted);
        }

        var message = Assert.Single(transport.Messages);
        Assert.Equal("dellago.avisos@example.com", Assert.Single(message.From.Mailboxes).Address);
        Assert.Equal("maria.perez@example.com", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("Asunto", message.Subject);
        Assert.Equal("Cuerpo", message.TextBody);
        Assert.Equal("disconnect", transport.Calls[^1]);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public void TheServer_IsGmailOn587_WithAThirtySecondTimeout()
    {
        Assert.Equal("smtp.gmail.com", MailKitTenantNoticeSender.Host);
        Assert.Equal(587, MailKitTenantNoticeSender.Port);
        Assert.Equal(30_000, MailKitTenantNoticeSender.TimeoutMilliseconds);
    }

    private sealed class SmtpCommandFailedForTest(string message) : Exception(message);

    private sealed class FakeTransport : ISmtpTransport
    {
        public Action? OnConnect { get; init; }

        public Action<string, string>? OnAuthenticate { get; init; }

        public Action<MimeMessage>? OnSend { get; init; }

        public List<string> Calls { get; } = [];

        public List<MimeMessage> Messages { get; } = [];

        public int Authentications { get; private set; }

        public bool Disposed { get; private set; }

        public Task ConnectAsync(string host, int port, CancellationToken ct)
        {
            Calls.Add($"connect {host}:{port}");
            OnConnect?.Invoke();
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(string userName, string password, CancellationToken ct)
        {
            Authentications++;
            Calls.Add($"auth {userName}");
            OnAuthenticate?.Invoke(userName, password);
            return Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message, CancellationToken ct)
        {
            Calls.Add("send");
            OnSend?.Invoke(message);
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct)
        {
            Calls.Add("disconnect");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
