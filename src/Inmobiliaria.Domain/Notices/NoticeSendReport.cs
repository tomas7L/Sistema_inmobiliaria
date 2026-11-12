namespace Inmobiliaria.Domain.Notices;

/// <summary>How one notice of a press ended (spec Requirement 5).</summary>
public enum NoticeSendStatus
{
    /// <summary>Gmail accepted the email and the sent row was committed.</summary>
    Sent,

    /// <summary>The database already held a sent row for this contract, kind and month: no email went out (Requirement 4).</summary>
    AlreadySent,

    /// <summary>The email was not accepted, and no sent row exists.</summary>
    Failed,
}

/// <summary>One notice's outcome. A failure carries its reason, already scrubbed of the password (design D7).</summary>
public sealed record NoticeSendOutcome(TenantNotice Notice, NoticeSendStatus Status, string? Reason)
{
    public static NoticeSendOutcome Sent(TenantNotice notice) => new(notice, NoticeSendStatus.Sent, null);

    public static NoticeSendOutcome AlreadySent(TenantNotice notice) => new(notice, NoticeSendStatus.AlreadySent, null);

    public static NoticeSendOutcome Failed(TenantNotice notice, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new(notice, NoticeSendStatus.Failed, reason);
    }
}

/// <summary>One press: an outcome per notice, and the counts the screen shows (spec Requirement 5).</summary>
public sealed record NoticeSendReport(IReadOnlyList<NoticeSendOutcome> Outcomes)
{
    public int SentCount => Outcomes.Count(o => o.Status == NoticeSendStatus.Sent);

    public int AlreadySentCount => Outcomes.Count(o => o.Status == NoticeSendStatus.AlreadySent);

    public IReadOnlyList<NoticeSendOutcome> Failures => Outcomes.Where(o => o.Status == NoticeSendStatus.Failed).ToList();
}
