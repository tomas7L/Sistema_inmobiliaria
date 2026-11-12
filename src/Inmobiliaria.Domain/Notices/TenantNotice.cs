namespace Inmobiliaria.Domain.Notices;

/// <summary>What a notice is about. The first and only kind is what the tenant owes (notificaciones spec P6).</summary>
public enum NoticeKind
{
    /// <summary>What the tenant owes on the sending day, stored as <c>amount_owed</c> (design §3.1).</summary>
    AmountOwed,
}

/// <summary>
/// One notice ready to send (notificaciones design §2.1): one email to one contract's current tenant about one month.
/// </summary>
/// <param name="AccountId">The contract's account.</param>
/// <param name="ContractId">The contract, which the sent record is unique on with the kind and the month.</param>
/// <param name="ContractShortId">The handle the operator knows the contract by, as the desk's search shows it.</param>
/// <param name="Tenant">The current tenant's name, in the desk search's words (spec Requirement 3).</param>
/// <param name="Units">The units, in the desk search's words.</param>
/// <param name="Recipient">The current tenant's email address.</param>
/// <param name="FiguresOn">The date the figures are for: the sending day (spec Decision 16).</param>
/// <param name="Total">The desk figure for a payment received that day (spec Decision 9). The recargo is inside it.</param>
/// <param name="Recargo">The part of <paramref name="Total"/> that is recargo, never an amount to add to it.</param>
/// <param name="DaysOverdue">The oldest unpaid period's days overdue, as the Mora list counts them; zero when not in mora.</param>
/// <param name="NoticeMonth">The 1st of the sending day's month.</param>
public sealed record TenantNotice(
    Guid AccountId,
    Guid ContractId,
    string ContractShortId,
    string Tenant,
    string Units,
    string Recipient,
    DateOnly FiguresOn,
    decimal Total,
    decimal Recargo,
    int DaysOverdue,
    DateOnly NoticeMonth)
{
    /// <summary>The month a notice sent on <paramref name="sendingDay"/> is for: the 1st of that month.</summary>
    public static DateOnly MonthOf(DateOnly sendingDay) => new(sendingDay.Year, sendingDay.Month, 1);
}

/// <summary>A contract that owes but whose tenant has no usable email (spec Decision 17), so the operator knows whom to call.</summary>
public sealed record UnreachableTenant(string ContractShortId, string Tenant, string Units);

/// <summary>A notice already sent for the month, with who sent it and when, as the database stamped them.</summary>
public sealed record SentNotice(string ContractShortId, string Tenant, string RecordedBy, DateTimeOffset RecordedAt);

/// <summary>Today's notices in three lists (spec Requirement 1): to send, no email, already sent.</summary>
public sealed record NoticeBoard(
    IReadOnlyList<TenantNotice> ToSend,
    IReadOnlyList<UnreachableTenant> NoEmail,
    IReadOnlyList<SentNotice> AlreadySent);
