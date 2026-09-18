namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// How an <see cref="AdjustmentClause"/>'s referenced indices combine into one coefficient.
/// Always derived from the clause's index count, never an independently settable field.
/// </summary>
public enum CombinationRule
{
    /// <summary>Exactly one referenced index; its own variation is the coefficient.</summary>
    Single,

    /// <summary>Two or more referenced indices; the coefficient averages their variations.</summary>
    Average,
}
