namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// How a computed canon is rounded to a chargeable figure. One member today, deliberately:
/// the spec's whole-peso rule (`whole-peso-amounts` convention) is the only rule Del Lago
/// uses. Extended by a migration when a second real rule appears, never guessed in advance.
/// </summary>
public enum RoundingRule
{
    /// <summary>Truncate toward zero to the nearest whole peso. Never commercial rounding.</summary>
    TruncateToWholePeso,
}
