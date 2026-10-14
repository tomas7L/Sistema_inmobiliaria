namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// An issued, numbered recibo: one paper copy of one payment. Immutable after construction, and it
/// reads nothing but itself: the lines, including the honorarios line, are stored as issued and no
/// member recomputes them from a contract or a rate (spec Decision 46). Not an EF entity yet; PR 5
/// maps it.
/// </summary>
public sealed class Recibo
{
    /// <summary>
    /// The one legend every recibo carries. It states that the document is not an invoice; its exact
    /// wording is for the agency's accountant to confirm (spec Decision 10).
    /// </summary>
    public const string NonFiscalLegend = "Documento no válido como factura";

    private readonly ReciboDraft _draft;

    public long Number { get; }

    public ReciboCopy Copy => _draft.Copy;

    /// <summary>The first day of the month this payment settles.</summary>
    public DateOnly Period { get; }

    /// <summary>The day the money was received, which is the day the recargo was measured to.</summary>
    public DateOnly ReceivedOn { get; }

    public ReciboHeader Header { get; }

    public IReadOnlyList<ReciboLine> Lines => _draft.Lines;

    /// <summary>The sum of the signed lines.</summary>
    public decimal Total => _draft.Total;

    public string Legend => NonFiscalLegend;

    public Recibo(long number, DateOnly period, DateOnly receivedOn, ReciboHeader header, ReciboDraft draft)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1L);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(draft);

        if (period.Day != 1)
        {
            throw new ArgumentException("A recibo's period is the first day of a month.", nameof(period));
        }

        Number = number;
        Period = period;
        ReceivedOn = receivedOn;
        Header = header;
        _draft = draft;
    }

    /// <summary>The lines as a draft, for comparing what was issued with what was previewed.</summary>
    public ReciboDraft AsDraft() => _draft;
}
