namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// The numbers collection and voiding are bound by, in one place. The database mirrors the same
/// figures in its triggers (a later slice), so a change here is a change there; the planners check
/// first and the database refuses what slips past them.
/// </summary>
public static class CollectionRules
{
    /// <summary>
    /// How many calendar days before today a received date may be (spec Decision 38). Exactly this
    /// many back is accepted, for either role; one more is refused.
    /// </summary>
    public const int BackDateBoundDays = 7;

    /// <summary>
    /// Up to this many calendar days after a payment was recorded, either role may void it
    /// (spec Decision 45).
    /// </summary>
    public const int VoidingFreeWindowDays = 7;

    /// <summary>
    /// Up to this many calendar days after a payment was recorded, an Admin may still void it; after
    /// that, nobody (spec Decision 45; weak, a stand-in for "until the period is liquidated to the owner").
    /// </summary>
    public const int VoidingAdminWindowDays = 30;
}
