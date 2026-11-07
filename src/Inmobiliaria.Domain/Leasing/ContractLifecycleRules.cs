using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// The bounds of notice and end as values, for the port and its summary (gestion-de-alquileres spec Requirement 10, design
/// §2.3). They live here and not on <see cref="Contract"/> because Domain has no clock.
/// </summary>
public static class ContractLifecycleRules
{
    /// <summary>
    /// The month an end on <paramref name="endDate"/> leaves uncharged, or null. A contract charges a month only when its due date
    /// falls on or before the end (<see cref="AccrualSchedule"/>), so an end before that month's due date leaves it to a new
    /// contract starting on its 1st (spec Decision 39, Requirement 10). A due date on or before the cutover was never the system's.
    /// </summary>
    public static DateOnly? MonthLeftUncharged(int dueDay, DateOnly? cutoverDate, DateOnly endDate)
    {
        var month = new DateOnly(endDate.Year, endDate.Month, 1);
        var due = AccrualSchedule.DueDateFor(month, dueDay);

        return due > endDate && !(cutoverDate is { } cutover && due <= cutover) ? month : null;
    }

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

        if (reason == EndReason.LoadedByMistake)
        {
            return ContractRefusal.EndReasonNotOffered;
        }

        if (endDate > today)
        {
            return ContractRefusal.EndAfterToday;
        }

        return endDate < startDate ? ContractRefusal.EndBeforeContractStart : null;
    }

    /// <summary>Why a notice cannot be undone, or null (spec Requirement 24).</summary>
    public static ContractRefusal? CheckUndoNotice(ContractStatus status, string? reason)
    {
        if (status != ContractStatus.PendingTermination)
        {
            return ContractRefusal.NoNoticeToUndo;
        }

        return string.IsNullOrWhiteSpace(reason) ? ContractRefusal.ReasonMissing : null;
    }

    /// <summary>Why an end cannot be undone as far as the contract alone can tell, or null. Whether its units are free is the adapter's.</summary>
    public static ContractRefusal? CheckUndoEnd(ContractStatus status, EndReason? endReason, string? reason)
    {
        if (status != ContractStatus.Ended)
        {
            return ContractRefusal.NoEndToUndo;
        }

        if (endReason == EndReason.LoadedByMistake)
        {
            return ContractRefusal.ContractCancelled;
        }

        return string.IsNullOrWhiteSpace(reason) ? ContractRefusal.ReasonMissing : null;
    }
}
