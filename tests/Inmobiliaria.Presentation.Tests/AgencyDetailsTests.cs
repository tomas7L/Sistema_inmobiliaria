using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec test 24c and Decision 37: missing agency data stops the print and is named; nothing is invented.</summary>
public class AgencyDetailsTests
{
    [Fact]
    public void EveryItemPresent_GivesTheDetails_AsConfigured()
    {
        var read = AgencyDetails.Read(PrintingTestKit.AgencyValues);

        Assert.Empty(read.Missing);
        Assert.Equal(
            ("Agencia de Prueba", "Calle Falsa 123, Ciudad", "0000 000000", "prueba@example.invalid", "CONDICION DE PRUEBA"),
            (read.Details!.Name, read.Details.Address, read.Details.Phone, read.Details.Email, read.Details.TaxStatus));
        Assert.Equal(
            ("Leyenda de prueba.", "CORREDOR DE PRUEBA", "TITULO DE PRUEBA", "000", "Assets/logo-de-prueba.png"),
            (read.Details.RentalActivityLegend, read.Details.BrokerName, read.Details.BrokerTitle, read.Details.LicenceNumber, read.Details.LogoFile));
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Address")]
    [InlineData("Phone")]
    [InlineData("Email")]
    [InlineData("TaxStatus")]
    [InlineData("RentalActivityLegend")]
    [InlineData("BrokerName")]
    [InlineData("BrokerTitle")]
    [InlineData("LicenceNumber")]
    [InlineData("LogoFile")]
    public void AMissingOrBlankItem_StopsAndIsNamed(string key)
    {
        foreach (var value in new[] { null, "", "   " })
        {
            var values = new Dictionary<string, string?>(PrintingTestKit.AgencyValues) { [key] = value };

            var read = AgencyDetails.Read(values);

            Assert.Null(read.Details);
            Assert.Equal(["Agency:" + key], read.Missing);
        }

        var absent = new Dictionary<string, string?>(PrintingTestKit.AgencyValues);
        absent.Remove(key);
        Assert.Equal(["Agency:" + key], AgencyDetails.Read(absent).Missing);
    }

    [Fact]
    public void TheProblemText_NamesEveryMissingItem()
    {
        var read = AgencyDetails.Read(new Dictionary<string, string?>());

        Assert.Equal(10, read.Missing.Count);
        Assert.StartsWith("No se puede imprimir: falta configurar ", read.Problem);
        Assert.Contains("Agency:LogoFile", read.Problem);
    }
}
