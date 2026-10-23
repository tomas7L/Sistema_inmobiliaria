using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Presentation.CashDesk;

/// <summary>
/// What an account's readiness tells the operator (spec Requirement 6, Decision 21): each missing gate named,
/// with what to do. The texts are the domain's own refusal texts for the same condition, so the picker and
/// the refusal at the commit never say two different things.
/// </summary>
public static class ReadinessText
{
    public const string Ready = "Lista para cobrar";

    public const string NotReady = "No está lista para cobrar";

    /// <summary>One line per missing gate, in a fixed order; empty when the account is ready.</summary>
    public static IReadOnlyList<string> Problems(AccountReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        var problems = new List<string>();
        if (!readiness.RateEntered)
        {
            problems.Add(CollectionRefusal.RateNeverEntered.OperatorText());
        }

        if (readiness.Frozen)
        {
            problems.Add(CollectionRefusal.AccountFrozen.OperatorText());
        }

        if (!readiness.StartingNumberSet)
        {
            problems.Add(CollectionRefusal.CutoverNotSet.OperatorText());
        }

        return problems;
    }
}
