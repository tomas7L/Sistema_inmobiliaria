namespace Inmobiliaria.Domain.Notices;

/// <summary>
/// Today's notices and sending them (notificaciones design D1, §3.3). Registered only with valid recargo terms, because the
/// desk figure needs them (spec Requirement 8).
/// </summary>
public interface ITenantNoticeDesk
{
    /// <summary>
    /// Brings every account up to date, then lists today's notices in three lists. Writes no sent row and sends nothing
    /// (spec Requirement 1).
    /// </summary>
    Task<NoticeBoard> ReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends <paramref name="notices"/> from the mailbox <paramref name="credentials"/> names, signed as
    /// <paramref name="agencyName"/>, each in its own transaction that commits only once the email is accepted (spec Decision
    /// 15). A failure on one never stops the next.
    /// </summary>
    Task<NoticeSendReport> SendAsync(
        IReadOnlyList<TenantNotice> notices, MailboxCredentials credentials, string agencyName, CancellationToken ct = default);
}

/// <summary>One email, already composed.</summary>
public sealed record OutgoingMail(string To, string Subject, string Body);

/// <summary>Whether a mailbox session opened, or why not. A failed login is an outcome, never an exception (design §2.1).</summary>
public sealed record MailSessionOpening(IMailSession? Session, string? FailureReason);

/// <summary>Whether one email was accepted, or why not, the reason already scrubbed of the password (design D7).</summary>
public sealed record MailDelivery(bool Accepted, string? FailureReason);

/// <summary>One authenticated mailbox session for a batch: one email at a time.</summary>
public interface IMailSession : IAsyncDisposable
{
    Task<MailDelivery> SendAsync(OutgoingMail mail, CancellationToken ct = default);
}

/// <summary>Sends notices by email (design D1). Logs in once per batch, before the database is touched (design D5).</summary>
public interface ITenantNoticeSender
{
    Task<MailSessionOpening> OpenAsync(MailboxCredentials credentials, CancellationToken ct = default);
}

/// <summary>Where this PC keeps the mailbox's address and app password (spec P4, design D9).</summary>
public interface IMailboxCredentialStore
{
    /// <summary>The stored credentials, or null when the mailbox is not set up on this PC.</summary>
    MailboxCredentials? Read();

    void Save(MailboxCredentials credentials);
}
