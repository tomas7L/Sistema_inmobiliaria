using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Recibos;

namespace Inmobiliaria.Presentation.Printing;

/// <summary>
/// Builds the printed sheet from the stored recibo (spec Requirement 24, test 41, design section 9.1): the
/// stored header, lines and total, the received date, the stored legend; nothing recomputed from the clock or
/// the contract. Importe Neto is the stored total, and the builder refuses a sheet whose printed amounts do
/// not add up to it.
/// </summary>
public static class ReciboSheetBuilder
{
    /// <summary>Both recibos of the payment, locatario first: two sheets.</summary>
    public static IReadOnlyList<ReciboSheet> BuildBoth(IssuedPayment payment, AgencyDetails agency) =>
        [Build(payment, ReciboCopy.Locatario, agency), Build(payment, ReciboCopy.Propietario, agency)];

    public static ReciboSheet Build(IssuedPayment payment, ReciboCopy copy, AgencyDetails agency)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(agency);

        var recibo = (copy == ReciboCopy.Locatario ? payment.Locatario : payment.Propietario).Recibo;
        var rows = PaperRows.For(recibo.Lines, recibo.Period);
        CheckTotal(rows, recibo.Total);

        var lessors = string.Join("; ", recibo.Header.Lessors);
        var payer = copy == ReciboCopy.Locatario ? recibo.Header.Tenant : lessors;
        var number = $"N° {recibo.Number}";

        var content = new ReciboHalfContent(
            agency,
            "RECIBO",
            number,
            ReciboLineLabels.CopyName(copy),
            Formats.Date(recibo.ReceivedOn),
            recibo.Legend,
            ReciboSheet.ServiceBand,
            recibo.Header.Tenant,
            lessors,
            recibo.Header.Units,
            ReciboSheet.CobroLine,
            rows,
            Formats.Money(recibo.Total),
            AmountInWords.Line(recibo.Total),
            $"que RECIBO de {payer} - C.U.I.T: -",
            ReciboSheet.IvaBoxLabels,
            agency.RentalActivityLegend,
            agency.BrokerName,
            agency.BrokerTitle,
            $"Matrícula N° {agency.LicenceNumber}",
            payment.Annulment is { } a
                ? new PrintedAnnulment("ANULADO", Formats.Date(DateOnly.FromDateTime(a.AnnulledAt.DateTime)), a.Reason)
                : null);

        return new ReciboSheet(number, new ReciboHalf("ORIGINAL", content), new ReciboHalf("DUPLICADO", content));
    }

    /// <summary>The printed amounts must add up to the stored total, or the sheet would say two things.</summary>
    public static void CheckTotal(IReadOnlyList<PrintedRow> rows, decimal storedTotal)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var printed = rows.Sum(r => r.Amount ?? 0m);
        if (printed != storedTotal)
        {
            throw new InvalidOperationException(
                $"The printed rows add up to {printed} but the stored total is {storedTotal}.");
        }
    }
}
