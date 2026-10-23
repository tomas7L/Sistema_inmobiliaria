using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>Obviously fake agency data: the real letterhead lives only in the shipped appsettings.json.</summary>
internal static class PrintingTestKit
{
    public static readonly Dictionary<string, string?> AgencyValues = new()
    {
        ["Name"] = "Agencia de Prueba",
        ["Address"] = "Calle Falsa 123, Ciudad",
        ["Phone"] = "0000 000000",
        ["Email"] = "prueba@example.invalid",
        ["TaxStatus"] = "CONDICION DE PRUEBA",
        ["RentalActivityLegend"] = "Leyenda de prueba.",
        ["BrokerName"] = "CORREDOR DE PRUEBA",
        ["BrokerTitle"] = "TITULO DE PRUEBA",
        ["LicenceNumber"] = "000",
        ["LogoFile"] = "Assets/logo-de-prueba.png",
    };

    public static AgencyDetails Agency() => AgencyDetails.Read(AgencyValues).Details!;
}
