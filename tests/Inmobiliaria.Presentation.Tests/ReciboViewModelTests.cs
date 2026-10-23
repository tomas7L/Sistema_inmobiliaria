using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Recibos;
using static Inmobiliaria.Presentation.Tests.CashDeskTestKit;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec Requirement 13 (display), task 4.8: both recibos as issued, and an annulment.</summary>
public class ReciboViewModelTests
{
    [Fact]
    public void BothRecibos_AreShownAsIssued_LocatarioFirst()
    {
        var recibo = new ReciboViewModel(Issued(Guid.NewGuid(), 31001));

        Assert.Equal(2, recibo.Copies.Count);
        var (locatario, propietario) = (recibo.Copies[0], recibo.Copies[1]);

        Assert.Equal(
            ("N° 31001", "Locatario", "Perez, Maria", "Gomez, Juan; Ibáñez, Rosa", "Bv. Belgrano 123, Sunchales", "01/2026", "15/01/2026"),
            (locatario.NumberText, locatario.CopyName, locatario.Tenant, locatario.Lessors, locatario.Units, locatario.PeriodText, locatario.ReceivedOnText));
        Assert.Equal([("Alquiler", "500.000"), ("Recargo por mora", "98.260")], locatario.Lines.Select(l => (l.Label, l.AmountText)));
        Assert.Equal("598.260", locatario.TotalText);
        Assert.Equal(Recibo.NonFiscalLegend, locatario.Legend);

        Assert.Equal(("N° 31002", "Propietario"), (propietario.NumberText, propietario.CopyName));
        Assert.Equal([("Alquiler", "500.000"), ("Honorarios de administración", "-47.860")], propietario.Lines.Select(l => (l.Label, l.AmountText)));
        Assert.Equal("452.140", propietario.TotalText);

        Assert.False(recibo.IsAnnulled);
        Assert.Null(recibo.AnnulmentText);
    }

    [Fact]
    public void AnAnnulledPayment_ShowsNumbersReasonUserAndTime_WithItsLinesStillReadable()
    {
        var annulment = new AnnulmentRecord("Cobrado por error", "Nicolás Demaría", new DateTimeOffset(2026, 1, 16, 9, 5, 0, TimeSpan.FromHours(-3)));

        var recibo = new ReciboViewModel(Issued(Guid.NewGuid(), 31001, annulment));

        Assert.True(recibo.IsAnnulled);
        Assert.Equal(
            "ANULADOS los recibos N° 31001 y 31002 el 16/01/2026 a las 09:05 por Nicolás Demaría. Motivo: Cobrado por error.",
            recibo.AnnulmentText);
        Assert.Equal(2, recibo.Copies[0].Lines.Count);
    }

    /// <summary>Every line kind has a Spanish label, so no stored line can reach the screen unnamed.</summary>
    [Fact]
    public void EveryLineKind_HasASpanishLabel()
    {
        foreach (var kind in Enum.GetValues<ReciboLineKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ReciboLineLabels.For(kind)));
        }
    }
}
