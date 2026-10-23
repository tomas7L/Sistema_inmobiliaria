using System.Globalization;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Recibos;

/// <summary>
/// A payment's two recibos on screen, exactly as issued (spec Requirement 13): what the reader returned,
/// formatted and nothing recomputed. An annulled pair also says when, by whom and why, and its lines stay
/// readable. Printing arrives in a later slice.
/// </summary>
public sealed class ReciboViewModel
{
    public ReciboViewModel(IssuedPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        Payment = payment;
        Copies = [new ReciboCopyView(payment.Locatario), new ReciboCopyView(payment.Propietario)];

        // The reader hands every time over already in the agency's zone (ReciboReader.InAgencyZone).
        if (payment.Annulment is { } annulment)
        {
            var at = annulment.AnnulledAt;
            AnnulmentText =
                $"ANULADOS los recibos N° {payment.Locatario.Recibo.Number} y {payment.Propietario.Recibo.Number} " +
                $"el {Formats.Date(DateOnly.FromDateTime(at.DateTime))} a las {at.ToString("HH:mm", CultureInfo.InvariantCulture)} " +
                $"por {annulment.AnnulledBy}. Motivo: {annulment.Reason}.";
        }
    }

    public IssuedPayment Payment { get; }

    /// <summary>Locatario first, then propietario.</summary>
    public IReadOnlyList<ReciboCopyView> Copies { get; }

    public bool IsAnnulled => Payment.IsAnnulled;

    /// <summary>When, by whom and why the pair was annulled; null for a live payment.</summary>
    public string? AnnulmentText { get; }
}

/// <summary>One recibo as the operator reads it.</summary>
public sealed class ReciboCopyView
{
    public ReciboCopyView(IssuedRecibo issued)
    {
        ArgumentNullException.ThrowIfNull(issued);

        var recibo = issued.Recibo;
        ReciboId = issued.ReciboId;
        NumberText = $"N° {recibo.Number}";
        CopyName = ReciboLineLabels.CopyName(recibo.Copy);
        Tenant = recibo.Header.Tenant;
        Lessors = string.Join("; ", recibo.Header.Lessors);
        Units = recibo.Header.Units;
        PeriodText = Formats.Period(recibo.Period);
        ReceivedOnText = Formats.Date(recibo.ReceivedOn);
        Lines = ReciboLineView.Of(recibo.Lines);
        TotalText = Formats.Money(recibo.Total);
        Legend = recibo.Legend;
    }

    public Guid ReciboId { get; }

    public string NumberText { get; }

    public string CopyName { get; }

    public string Tenant { get; }

    public string Lessors { get; }

    public string Units { get; }

    public string PeriodText { get; }

    public string ReceivedOnText { get; }

    /// <summary>In stored order.</summary>
    public IReadOnlyList<ReciboLineView> Lines { get; }

    public string TotalText { get; }

    public string Legend { get; }
}
