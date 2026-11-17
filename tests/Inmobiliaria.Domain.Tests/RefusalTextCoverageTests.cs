using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Units;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Test 71 (Requirement 22), the audit of task 12.4: each refusal has its own operator text, so
/// none reaches the generic error. Persons, contracts and indices already pin it beside their rules
/// (<c>PartyIdentifiersTests</c>, <c>ContractDraftRulesTests</c>, <c>IndexEntryRulesTests</c>); these are the other two.
/// A refusal added without a text throws in its switch, which these tests catch before an operator does.
/// </summary>
public class RefusalTextCoverageTests
{
    [Fact]
    public void EveryUnitRefusal_HasItsOwnOperatorText()
    {
        var texts = Enum.GetValues<UnitRefusal>().Select(refusal => refusal.OperatorText("Sarmiento 201")).ToList();

        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    [Fact]
    public void EveryAdjustmentRefusal_HasItsOwnOperatorText()
    {
        var texts = Enum.GetValues<AdjustmentRefusal>().Select(refusal => refusal.OperatorText()).ToList();

        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }
}
