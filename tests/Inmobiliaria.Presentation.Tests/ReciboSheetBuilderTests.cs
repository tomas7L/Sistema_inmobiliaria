using System.Text.Json;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Printing;
using static Inmobiliaria.Presentation.Tests.CashDeskTestKit;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec tests 24 and 41 (print model half), Decisions 15 to 18, design section 9.1.</summary>
public class ReciboSheetBuilderTests
{
    private static readonly AnnulmentRecord Annulment =
        new("Cobrado por error", "Nicolás", new DateTimeOffset(2026, 1, 16, 9, 5, 0, TimeSpan.FromHours(-3)));

    private static ReciboSheet Sheet(ReciboCopy copy, AnnulmentRecord? annulment = null) =>
        ReciboSheetBuilder.Build(Issued(Guid.NewGuid(), 31001, annulment), copy, PrintingTestKit.Agency());

    private static string Json(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void OriginalAndDuplicado_AreIdentical_ExceptTheCaption()
    {
        var sheet = Sheet(ReciboCopy.Locatario);

        Assert.Equal(("ORIGINAL", "DUPLICADO"), (sheet.Original.Caption, sheet.Duplicado.Caption));
        Assert.Equal(Json(sheet.Original.Content), Json(sheet.Duplicado.Content));
    }

    [Fact]
    public void AHalf_CarriesTheRecibo_TheLegends_AndTheReceivedDate()
    {
        var half = Sheet(ReciboCopy.Locatario).Original.Content;

        Assert.Equal(
            ("RECIBO", "N° 31001", "Locatario", "15/01/2026", Recibo.NonFiscalLegend, "Leyenda de prueba."),
            (half.Title, half.NumberText, half.RecipientType, half.DateText, half.NonFiscalLegend, half.RentalActivityLegend));
        Assert.Equal(ReciboSheet.ServiceBand, half.ServiceBand);
        Assert.Equal("COBRO de ALQUILER/SERVICIOS e IMPUESTOS por CUENTA y ORDEN de TERCEROS", ReciboSheet.ServiceBand);
        Assert.Equal(("Perez, Maria", "Gomez, Juan; Ibáñez, Rosa", "Bv. Belgrano 123, Sunchales"), (half.Tenant, half.Lessors, half.Units));
        Assert.Equal(("Agencia de Prueba", "CORREDOR DE PRUEBA", "TITULO DE PRUEBA", "Matrícula N° 000"), (half.Agency.Name, half.BrokerName, half.BrokerTitle, half.LicenceText));
        Assert.Equal(["Resp. Insc.", "Resp. Monotributo", "No Resp.", "Cons. Final", "Exento"], half.IvaBoxes);
    }

    /// <summary>Spec test 41: Importe Neto is the stored total and the words say it; the payer line names who pays.</summary>
    [Fact]
    public void ImporteNeto_IsTheStoredTotal_AndTheWordsSayIt()
    {
        var locatario = Sheet(ReciboCopy.Locatario).Original.Content;
        var propietario = Sheet(ReciboCopy.Propietario).Original.Content;

        Assert.Equal(("598.260", AmountInWords.Line(598_260m)), (locatario.ImporteNetoText, locatario.AmountInWordsText));
        Assert.Equal(("452.140", AmountInWords.Line(452_140m)), (propietario.ImporteNetoText, propietario.AmountInWordsText));
        Assert.Equal("que RECIBO de Perez, Maria - C.U.I.T: -", locatario.PayerLine);
        Assert.Equal("que RECIBO de Gomez, Juan; Ibáñez, Rosa - C.U.I.T: -", propietario.PayerLine);
    }

    /// <summary>Spec test 24: the pair's two sheets are separate recibos and never share a line.</summary>
    [Fact]
    public void ThePairsTwoSheets_NeverShareALine()
    {
        var both = ReciboSheetBuilder.BuildBoth(Issued(Guid.NewGuid(), 31001), PrintingTestKit.Agency());

        Assert.Equal(["N° 31001", "N° 31002"], both.Select(s => s.Original.Content.NumberText));
        var honorarios = both.Select(s => s.Original.Content.Rows.Single(r => r.Label == "Honorarios").AmountText).ToList();
        Assert.Equal(["0", "-47.860"], honorarios);
        var recargo = both.Select(s => s.Original.Content.Rows.Single(r => r.Label == "Recargo").AmountText).ToList();
        Assert.Equal(["98.260", "0"], recargo);
    }

    /// <summary>Spec test 24 and Decision 17: two builds of a live recibo are equal and carry no reprint mark or annulment.</summary>
    [Fact]
    public void TwoBuildsOfALiveRecibo_AreEqual_AndUnmarked()
    {
        var payment = Issued(Guid.NewGuid(), 31001);

        var first = ReciboSheetBuilder.Build(payment, ReciboCopy.Locatario, PrintingTestKit.Agency());
        var second = ReciboSheetBuilder.Build(payment, ReciboCopy.Locatario, PrintingTestKit.Agency());

        Assert.Equal(Json(first), Json(second));
        Assert.Null(first.Original.Content.Annulment);
        Assert.DoesNotContain("reimpres", Json(first), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Spec test 24 and Decision 18: an annulled recibo shows ANULADO, the date and the reason on both halves.</summary>
    [Theory]
    [InlineData(ReciboCopy.Locatario)]
    [InlineData(ReciboCopy.Propietario)]
    public void AnAnnulledRecibo_IsMarkedOnBothHalves(ReciboCopy copy)
    {
        var sheet = Sheet(copy, Annulment);

        foreach (var half in new[] { sheet.Original.Content, sheet.Duplicado.Content })
        {
            Assert.Equal(new PrintedAnnulment("ANULADO", "16/01/2026", "Cobrado por error"), half.Annulment);
            Assert.NotEmpty(half.Rows);
        }
    }

    /// <summary>D12: the builder refuses a sheet whose printed amounts do not add up to the stored total.</summary>
    [Fact]
    public void PrintedAmountsThatDoNotAddUp_AreRefused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReciboSheetBuilder.CheckTotal([new PrintedRow("Alquiler", 100m, "100")], storedTotal: 101m));
    }
}
