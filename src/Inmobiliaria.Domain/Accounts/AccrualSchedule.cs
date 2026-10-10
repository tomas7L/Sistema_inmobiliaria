using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Accounts;

/// <summary>One period a contract owes rent for, at the canon that was in force when it fell due.</summary>
/// <param name="Period">The month the rent covers, as the first day of that month.</param>
/// <param name="DueDate">The day it fell due, from which mora is counted.</param>
/// <param name="Canon">The canon in force for this period. Reconstructed, never guessed.</param>
public sealed record AccrualPeriod(DateOnly Period, DateOnly DueDate, decimal Canon);

/// <summary>
/// Which periods a contract owes rent for by a given date, and at what canon each one fell due.
///
/// This is the question the lazy accrual asks (design Decision 1). Nothing here writes: it answers
/// what SHOULD exist, and the caller compares that against what the account already holds.
/// </summary>
public static class AccrualSchedule
{
    /// <summary>
    /// Every period that has fallen due on or before <paramref name="asOf"/>, oldest first.
    /// </summary>
    public static IReadOnlyList<AccrualPeriod> PeriodsDue(Contract contract, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var periods = new List<AccrualPeriod>();
        var period = FirstOfMonth(contract.StartDate);

        // A contract that ended stops producing periods. That is what makes a closed account need
        // no flag: there is simply nothing further to come due (design Decision 6).
        var lastDay = contract.ActualEndDate is { } ended && ended < asOf ? ended : asOf;

        while (period <= lastDay)
        {
            var dueDate = DueDateFor(period, contract.DueDay);

            // A period counts only once its due date has arrived, and only if that date fell
            // while the contract existed. A lease signed on the 20th does not owe a due date that
            // passed on the 10th — the contract was not yet in force. This is NOT proration: if
            // the agency charges a part-month for the days actually occupied, that is a separate
            // charge somebody records, not a period this schedule invents.
            if (dueDate <= lastDay && dueDate >= contract.StartDate)
            {
                periods.Add(new AccrualPeriod(period, dueDate, CanonFor(contract, period)));
            }

            period = period.AddMonths(1);
        }

        return periods;
    }

    /// <summary>
    /// The day <paramref name="period"/> falls due, given a contract's due day.
    ///
    /// A due day longer than the month clamps to that month's last day: a lease that says the 31st
    /// falls due on 28 February, because the alternative is either rejecting the lease or rolling
    /// into March and charging a month of mora the tenant could not have avoided.
    /// </summary>
    public static DateOnly DueDateFor(DateOnly period, int dueDay)
    {
        if (dueDay is < 1 or > 31)
        {
            throw new ArgumentException(
                "A due day must be a day of the month, between 1 and 31.", nameof(dueDay));
        }

        var daysInMonth = DateTime.DaysInMonth(period.Year, period.Month);

        return new DateOnly(period.Year, period.Month, Math.Min(dueDay, daysInMonth));
    }

    /// <summary>
    /// The canon in force for <paramref name="period"/>, reconstructed from the contract's
    /// append-only adjustment history rather than read off <see cref="Contract.MonthlyRent"/>.
    ///
    /// MonthlyRent holds the CURRENT canon and is overwritten every time an adjustment is
    /// confirmed, so reading it for a past period would re-price history — the one thing the
    /// specification forbids outright. The history makes this recoverable: each adjustment carries
    /// the canon it replaced as well as the canon it set.
    /// </summary>
    public static decimal CanonFor(Contract contract, DateOnly period)
    {
        ArgumentNullException.ThrowIfNull(contract);

        // An adjustment effective ON a period's due date applies to that period. Comparing
        // against the period's first day would be wrong for a contract whose adjustment lands
        // mid-month, and comparing against the due date is what "the canon in force when it fell
        // due" actually means.
        var dueDate = DueDateFor(period, contract.DueDay);

        var inForce = contract.Adjustments
            .Where(a => a.EffectiveDate <= dueDate)
            .OrderByDescending(a => a.EffectiveDate)
            .FirstOrDefault();

        if (inForce is not null)
        {
            return inForce.NewCanon;
        }

        // No adjustment had taken effect yet. The original canon is NOT MonthlyRent once any
        // adjustment has been confirmed, because that call overwrote it — it is the canon the
        // earliest adjustment replaced.
        var earliest = contract.Adjustments
            .OrderBy(a => a.EffectiveDate)
            .FirstOrDefault();

        return earliest?.PreviousCanon ?? contract.MonthlyRent;
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
