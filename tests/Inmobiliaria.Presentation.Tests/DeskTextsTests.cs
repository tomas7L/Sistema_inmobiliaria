using Inmobiliaria.Presentation.CashDesk;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirement 19: every label naming what the period's balance covers names the difference too.</summary>
public class DeskTextsTests
{
    [Fact]
    public void TheHandedAmountLabel_NamesEveryConceptTheBalanceCovers()
    {
        Assert.Equal(
            "Importe entregado por el inquilino (se compara con el saldo del período: alquiler, cuota, recargo y diferencia de ajuste)",
            DeskTexts.HandedAmountLabel);
    }
}
