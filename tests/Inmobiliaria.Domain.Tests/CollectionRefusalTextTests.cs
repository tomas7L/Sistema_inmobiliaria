using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// ui-de-cobranza spec test 13, domain half (design section 6.4, Finding F1): the desk raises
/// <see cref="CollectionRefusal.CutoverNotSet"/> when the recibo counter has no starting number, so its
/// operator text must say that, and must never send the operator to a contract's cutover date.
/// </summary>
public class CollectionRefusalTextTests
{
    [Fact]
    public void CutoverNotSet_NamesTheStartingReciboNumber_AndNotTheCutoverDate()
    {
        var text = CollectionRefusal.CutoverNotSet.OperatorText();

        Assert.Contains("número inicial de recibo", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Puesta en marcha", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fecha de corte", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contrato", text, StringComparison.OrdinalIgnoreCase);
    }
}
