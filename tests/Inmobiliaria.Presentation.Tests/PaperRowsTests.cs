using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec test 24a, Decisions 34 and 35, design D12: the paper's fixed rows.</summary>
public class PaperRowsTests
{
    private static readonly DateOnly Period = new(2026, 1, 1);

    private static IReadOnlyList<PrintedRow> Rows(params ReciboLine[] lines) => PaperRows.For(lines, Period);

    [Fact]
    public void TheFixedRows_PrintInPaperOrder_WithTheRentsPeriod()
    {
        var rows = Rows(new ReciboLine(ReciboLineKind.Rent, 1, 500_000m));

        Assert.Equal(
            [
                "Alquiler 01/2026", "Tasa Municipal", "Sellado Provincial", "Imp. Inmobiliario", "Aguas Prov. Sta. Fe",
                "Litoral Gas", "Luz EPE", "CUOTA 1", "CUOTA 2", "Dep. en Garantía", "Recargo", "Reserva", "Expensas",
                "Saldo a Favor", "Saldo Deudor", "Honorarios", "Otros Conceptos",
            ],
            rows.Select(r => r.Label));
    }

    [Fact]
    public void AModelledRowWithNoLine_PrintsZero_AndAnUnmodelledRow_PrintsNoAmount()
    {
        var rows = Rows(new ReciboLine(ReciboLineKind.Rent, 1, 500_000m)).ToDictionary(r => r.Label, r => r.AmountText);

        Assert.Equal("500.000", rows["Alquiler 01/2026"]);
        foreach (var modelled in new[] { "Tasa Municipal", "Recargo", "Saldo Deudor", "Honorarios", "Otros Conceptos" })
        {
            Assert.Equal("0", rows[modelled]);
        }

        foreach (var unmodelled in new[] { "Sellado Provincial", "Imp. Inmobiliario", "Aguas Prov. Sta. Fe", "Litoral Gas", "Luz EPE", "CUOTA 1", "CUOTA 2", "Dep. en Garantía", "Reserva", "Expensas", "Saldo a Favor" })
        {
            Assert.Equal(string.Empty, rows[unmodelled]);
        }
    }

    [Fact]
    public void ALineWithNoPaperRow_PrintsOnAnAddedRow_AfterOtrosConceptos()
    {
        var rows = Rows(
            new ReciboLine(ReciboLineKind.Rent, 1, 500_000m),
            new ReciboLine(ReciboLineKind.ContractualFeeInstalment, 2, 20_000m),
            new ReciboLine(ReciboLineKind.Seguro, 3, -4_000m));

        Assert.Equal(
            [("Otros Conceptos", "0"), ("Cuota de honorarios contractuales", "20.000"), ("Seguro", "-4.000")],
            rows.Skip(16).Select(r => (r.Label, r.AmountText)));
    }

    [Fact]
    public void TheHonorariosLine_PrintsItsNegativeAmount()
    {
        var rows = Rows(new ReciboLine(ReciboLineKind.Rent, 1, 500_000m), new ReciboLine(ReciboLineKind.HonorariosAdministracion, 2, -47_860m));

        Assert.Equal("-47.860", rows.Single(r => r.Label == "Honorarios").AmountText);
    }

    [Fact]
    public void EveryLineKind_LandsOnARow()
    {
        foreach (var kind in Enum.GetValues<ReciboLineKind>())
        {
            var rows = Rows(new ReciboLine(kind, 1, kind == ReciboLineKind.HonorariosAdministracion ? -1m : 1m));
            Assert.Single(rows, r => r.Amount is 1m or -1m);
        }
    }
}
