using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// The first period a confirmed adjustment prices (gestion-de-alquileres spec Requirement 15, design §3.2). The adjustment's
/// due date keeps driving the calendar; this period alone drives pricing.
/// </summary>
public static class AdjustmentTiming
{
    /// <summary>
    /// The month of <paramref name="confirmedOn"/>, moved forward while that month's due date has arrived, since the tenant
    /// may already be paying that day, or while the month already carries its charge, an early payment included.
    /// </summary>
    public static DateOnly AppliesFrom(Contract contract, ContractAccount account, DateOnly confirmedOn)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(account);

        var period = new DateOnly(confirmedOn.Year, confirmedOn.Month, 1);

        while (AccrualSchedule.DueDateFor(period, contract.DueDay) <= confirmedOn || account.IsChargedFor(period))
        {
            period = period.AddMonths(1);
        }

        return period;
    }

    /// <summary>Late means it prices from a later month than the one it fell due in.</summary>
    public static bool IsLate(DateOnly effectiveDate, DateOnly appliesFrom) =>
        appliesFrom > new DateOnly(effectiveDate.Year, effectiveDate.Month, 1);
}
