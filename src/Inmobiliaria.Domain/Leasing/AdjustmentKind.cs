namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Distinguishes an ordinary confirmed adjustment from one that corrects the effect of a
/// previously confirmed adjustment whose source index value was later corrected (spec
/// "A Correction Produces a New Adjustment, Never a Rewrite").
/// </summary>
public enum AdjustmentKind
{
    /// <summary>A normal adjustment confirmed on its own schedule.</summary>
    Regular,

    /// <summary>Corrects a prior confirmed adjustment; always names <c>CorrectsAdjustmentId</c>.</summary>
    Correction,
}
