using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Presentation.Voiding;

/// <summary>Whether the screen offers a voiding, and why not when it does not.</summary>
public sealed record VoidingGateResult(bool Offered, string? Explanation);

/// <summary>
/// The voiding screen's pre-gate (spec Requirement 16, Decision 28, design section 8): the age of the pair and
/// the operator's role, with the same arithmetic and constants as <see cref="VoidingPlanner"/>, explained in the
/// domain's own refusal texts. It offers or withholds; it never decides. What the operator is allowed to
/// attempt still goes to the desk, and the desk's and the database's refusals are what is shown.
/// </summary>
public static class VoidingGate
{
    public static VoidingGateResult For(DateOnly recordedOn, DateOnly today, UserRole role, bool isAnnulled)
    {
        if (isAnnulled)
        {
            return Withheld(VoidingRefusal.AlreadyAnnulled);
        }

        var age = today.DayNumber - recordedOn.DayNumber;
        if (age > CollectionRules.VoidingAdminWindowDays)
        {
            return Withheld(VoidingRefusal.WindowClosed);
        }

        if (age > CollectionRules.VoidingFreeWindowDays && role != UserRole.Admin)
        {
            return Withheld(VoidingRefusal.AdminOnly);
        }

        return new VoidingGateResult(true, null);
    }

    private static VoidingGateResult Withheld(VoidingRefusal refusal) => new(false, refusal.OperatorText());
}
