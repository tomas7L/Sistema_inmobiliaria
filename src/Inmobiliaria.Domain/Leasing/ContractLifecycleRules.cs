namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// The bounds of notice and end as values, for the port and its summary (gestion-de-alquileres spec Requirement 10, design
/// §2.3). They live here and not on <see cref="Contract"/> because Domain has no clock.
/// </summary>
public static class ContractLifecycleRules
{
    /// <summary>Why notice cannot be recorded, or null: only from <c>Active</c>, not after today, and a move-out not before it.</summary>
    public static ContractRefusal? CheckNotice(ContractStatus status, DateOnly noticeDate, DateOnly moveOutDate, DateOnly today)
    {
        if (status != ContractStatus.Active)
        {
            return ContractRefusal.NoticeNeedsActive;
        }

        if (noticeDate > today)
        {
            return ContractRefusal.NoticeAfterToday;
        }

        return moveOutDate < noticeDate ? ContractRefusal.MoveOutBeforeNotice : null;
    }

    /// <summary>Why it cannot be ended, or null: not already ended, a reason, and a date from its start to today. Debt never appears here.</summary>
    public static ContractRefusal? CheckEnd(ContractStatus status, DateOnly startDate, EndReason? reason, DateOnly endDate, DateOnly today)
    {
        if (status == ContractStatus.Ended)
        {
            return ContractRefusal.ContractEnded;
        }

        if (reason is null)
        {
            return ContractRefusal.EndReasonMissing;
        }

        if (endDate > today)
        {
            return ContractRefusal.EndAfterToday;
        }

        return endDate < startDate ? ContractRefusal.EndBeforeContractStart : null;
    }
}
