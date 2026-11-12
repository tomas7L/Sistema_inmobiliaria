namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// One row of <c>tenant_notices</c>, the sent record (notificaciones design §3.1, D4): a notice went out to one contract's
/// tenant for one month. Append-only, unique per contract, kind and month, and stamped by the database with who sent it and
/// when; whatever an insert supplies for those two is overwritten (spec Requirement 7).
/// </summary>
public sealed class TenantNoticeEntry
{
    /// <summary>The stored value of <see cref="Domain.Notices.NoticeKind.AmountOwed"/>.</summary>
    public const string AmountOwedKind = "amount_owed";

    public Guid Id { get; init; }

    public Guid ContractId { get; init; }

    public string Kind { get; init; } = AmountOwedKind;

    /// <summary>The 1st of the sending day's month.</summary>
    public DateOnly NoticeMonth { get; init; }

    public string Recipient { get; init; } = string.Empty;

    /// <summary>The date the notice's figures are for.</summary>
    public DateOnly FiguresOn { get; init; }

    public decimal Total { get; init; }

    public decimal Recargo { get; init; }

    /// <summary>Stamped by the database.</summary>
    public DateTimeOffset RecordedAt { get; init; }

    /// <summary>The inserting <c>current_user</c>, stamped by the database.</summary>
    public string RecordedBy { get; init; } = string.Empty;
}
