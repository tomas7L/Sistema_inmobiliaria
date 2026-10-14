namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// The ledger date rule: a movement about a period is never dated before that period was charged
/// (cobranza-y-recibos design D2). Pure functions of two dates.
///
/// For an ordinary late payment both rules collapse to the plain answer, the received date and the
/// voiding day, which is what the spec describes. They differ only for an early payment and for a
/// back-dated payment of an opening-balance month typed after the money arrived. The received date
/// itself is stored on the collection and printed on the recibo; these dates are the ledger's.
/// </summary>
public static class LedgerDating
{
    /// <summary>
    /// The date of a <see cref="MovementKind.Payment"/> or <see cref="MovementKind.RecargoCharged"/>:
    /// the later of the day the money was received and the period's charge date (the date of its
    /// rent accrual or opening balance).
    /// </summary>
    public static DateOnly PaymentDate(DateOnly receivedOn, DateOnly chargeDate) =>
        receivedOn >= chargeDate ? receivedOn : chargeDate;

    /// <summary>
    /// The same date once the period already holds corrections: also never before the latest
    /// <see cref="MovementKind.Correction"/> on it (<paramref name="latestCorrectionOn"/>, null when
    /// there is none). A payment entered again after a voiding is thereby dated after the corrections
    /// that settled the first one, so no dated view sees two payments and no corrections. Only the
    /// ledger date moves; the received date and the recargo measured to it do not.
    /// </summary>
    public static DateOnly PaymentDate(DateOnly receivedOn, DateOnly chargeDate, DateOnly? latestCorrectionOn)
    {
        var date = PaymentDate(receivedOn, chargeDate);

        return latestCorrectionOn is { } correctedOn && correctedOn > date ? correctedOn : date;
    }

    /// <summary>
    /// The date of the <see cref="MovementKind.Correction"/> a voiding writes: the later of the day
    /// of the voiding and the date of the movement it reverses, so a reversal never precedes the
    /// payment it undoes.
    /// </summary>
    public static DateOnly CorrectionDate(DateOnly voidingDay, DateOnly reversedMovementDate) =>
        voidingDay >= reversedMovementDate ? voidingDay : reversedMovementDate;
}
