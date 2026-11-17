namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// The largest figures an adjustment can store (spec Test 71, task 12.4). A variation and the coefficient are kept to six
/// decimals in twelve digits, a canon to cents in fourteen. A level typed far off and accepted past the far-level warning can
/// produce a figure past them; checked first, it is refused in the operator's words instead of reaching the generic error.
/// </summary>
public static class AdjustmentLimits
{
    /// <summary>The largest variation or coefficient, in percent: <c>numeric(12,6)</c>.</summary>
    public const decimal MaximumPercent = 999_999.999999m;

    /// <summary>The largest canon: <c>numeric(14,2)</c>.</summary>
    public const decimal MaximumCanon = 999_999_999_999.99m;

    /// <summary>Whether every index's variation and the coefficient fit, checked before any canon is computed from them.</summary>
    public static bool PercentsFit(decimal coefficient, IEnumerable<RentAdjustmentIndexValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return PercentFits(coefficient) && values.All(value => PercentFits(value.Variation));
    }

    /// <summary>Whether a new canon can be stored: more than zero, a contract's rent always is, and within its column.</summary>
    public static bool CanonFits(decimal canon) => canon > 0m && canon <= MaximumCanon;

    /// <summary>Whether a charged difference fits a movement's amount.</summary>
    public static bool AmountFits(decimal amount) => amount <= MaximumCanon;

    private static bool PercentFits(decimal percent) => Math.Abs(percent) <= MaximumPercent;
}
