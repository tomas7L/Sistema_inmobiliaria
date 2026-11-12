using System.Collections.Concurrent;
using Inmobiliaria.Domain.Notices;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// The mail port, faked (notificaciones spec: no test sends a real email). It counts logins and every send it was asked for,
/// keeps the emails it accepted, and lets a test refuse a login, refuse one send, or hold a send until something happens.
/// </summary>
internal sealed class FakeNoticeSender : ITenantNoticeSender
{
    private int _opens;
    private int _attempts;

    /// <summary>When set, every login is refused with this reason.</summary>
    public string? LoginFailure { get; init; }

    /// <summary>
    /// Decides each send, given the email and its 1-based attempt number across every session; null accepts them all.
    /// </summary>
    public Func<OutgoingMail, int, Task<MailDelivery>>? OnSend { get; init; }

    public int Opens => _opens;

    /// <summary>How many sends were asked for, accepted or not.</summary>
    public int Attempts => _attempts;

    /// <summary>The emails accepted, in the order they were.</summary>
    public ConcurrentQueue<OutgoingMail> Accepted { get; } = new();

    public Task<MailSessionOpening> OpenAsync(MailboxCredentials credentials, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _opens);

        return Task.FromResult(LoginFailure is { } reason
            ? new MailSessionOpening(null, reason)
            : new MailSessionOpening(new Session(this), null));
    }

    private sealed class Session(FakeNoticeSender owner) : IMailSession
    {
        public async Task<MailDelivery> SendAsync(OutgoingMail mail, CancellationToken ct = default)
        {
            var attempt = Interlocked.Increment(ref owner._attempts);
            var delivery = owner.OnSend is null ? new MailDelivery(true, null) : await owner.OnSend(mail, attempt);

            if (delivery.Accepted)
            {
                owner.Accepted.Enqueue(mail);
            }

            return delivery;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
