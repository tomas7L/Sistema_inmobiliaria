using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Recibos;

namespace Inmobiliaria.Presentation.Printing;

/// <summary>One printed row: its label and amount. A null amount is a row the system does not model: no figure.</summary>
public sealed record PrintedRow(string Label, decimal? Amount, string AmountText);

/// <summary>
/// The agency's paper rows as data, not code paths (spec Decisions 34, 35, design D12). A modelled row prints
/// its stored amount, or 0 when the recibo has no such line; a row the system does not model prints its label
/// with no amount, because 0 would state something the system does not know; a stored line whose kind has no
/// row (Seguro, the contractual instalment) prints on an added row after Otros Conceptos, so the printed
/// amounts always add up to the stored total.
/// </summary>
public static class PaperRows
{
    /// <summary>The paper's rows in paper order, each with the line kind it shows, or none.</summary>
    public static readonly IReadOnlyList<(string Label, ReciboLineKind? Kind)> Fixed =
    [
        ("Alquiler", ReciboLineKind.Rent),
        ("Tasa Municipal", ReciboLineKind.TasaMunicipal),
        ("Sellado Provincial", null),
        ("Imp. Inmobiliario", null),
        ("Aguas Prov. Sta. Fe", null),
        ("Litoral Gas", null),
        ("Luz EPE", null),
        ("CUOTA 1", null),
        ("CUOTA 2", null),
        ("Dep. en Garantía", null),
        ("Recargo", ReciboLineKind.Recargo),
        ("Reserva", null),
        ("Expensas", null),
        ("Saldo a Favor", null),
        ("Saldo Deudor", ReciboLineKind.SaldoDeudor),
        ("Honorarios", ReciboLineKind.HonorariosAdministracion),
        ("Otros Conceptos", ReciboLineKind.OtrosConceptos),
    ];

    public static IReadOnlyList<PrintedRow> For(IReadOnlyList<ReciboLine> lines, DateOnly period)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rows = new List<PrintedRow>();
        foreach (var (label, kind) in Fixed)
        {
            var text = kind == ReciboLineKind.Rent ? $"{label} {Formats.Period(period)}" : label;
            if (kind is { } modelled)
            {
                var amount = lines.Where(l => l.Kind == modelled).Sum(l => l.Amount);
                rows.Add(new PrintedRow(text, amount, Formats.Money(amount)));
            }
            else
            {
                rows.Add(new PrintedRow(text, null, string.Empty));
            }
        }

        var onPaper = Fixed.Where(f => f.Kind is not null).Select(f => f.Kind!.Value).ToHashSet();
        foreach (var line in lines.Where(l => !onPaper.Contains(l.Kind)))
        {
            rows.Add(new PrintedRow(ReciboLineLabels.For(line.Kind), line.Amount, Formats.Money(line.Amount)));
        }

        return rows;
    }
}
