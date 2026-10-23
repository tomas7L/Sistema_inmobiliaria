namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Why <see cref="Contract.CheckCutoverDate"/> refuses a cutover date (ui-de-cobranza design section 6.1).
/// A value, so the go-live screen shows it as operator text instead of catching an exception.
/// </summary>
public enum CutoverRefusal
{
    /// <summary>The date is after the agency's today.</summary>
    AfterToday,

    /// <summary>The date is before the contract's start date.</summary>
    BeforeContractStart,

    /// <summary>The account already holds a movement, so the date is locked.</summary>
    AccountHasMovement,
}
