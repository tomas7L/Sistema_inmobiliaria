using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Recibos;

/// <summary>The Spanish name of each recibo line kind, the same on the desk's preview and on the recibo display.</summary>
public static class ReciboLineLabels
{
    public static string For(ReciboLineKind kind) => kind switch
    {
        ReciboLineKind.Rent => "Alquiler",
        ReciboLineKind.ContractualFeeInstalment => "Cuota de honorarios contractuales",
        ReciboLineKind.Recargo => "Recargo por mora",
        ReciboLineKind.TasaMunicipal => "Tasa municipal",
        ReciboLineKind.Seguro => "Seguro",
        ReciboLineKind.OtrosConceptos => "Otros conceptos",
        ReciboLineKind.SaldoDeudor => "Saldo deudor",
        ReciboLineKind.HonorariosAdministracion => "Honorarios de administración",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A line kind without a label."),
    };

    public static string CopyName(ReciboCopy copy) => copy switch
    {
        ReciboCopy.Locatario => "Locatario",
        ReciboCopy.Propietario => "Propietario",
        _ => throw new ArgumentOutOfRangeException(nameof(copy), copy, "A copy without a name."),
    };
}

/// <summary>One recibo line as the operator reads it: its label and its signed amount.</summary>
public sealed record ReciboLineView(string Label, string AmountText)
{
    public static IReadOnlyList<ReciboLineView> Of(IEnumerable<ReciboLine> lines) =>
        lines.Select(l => new ReciboLineView(ReciboLineLabels.For(l.Kind), Formats.Money(l.Amount))).ToList();
}
