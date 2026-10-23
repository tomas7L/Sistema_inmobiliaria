using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Presentation.Printing;

/// <summary>How a print request ended. Cancelling the dialog is not a failure: nothing happened.</summary>
public enum PrintOutcome
{
    Printed,
    Cancelled,
}

/// <summary>
/// The printing seam (design section 9.1): Desktop draws the sheets and opens the Windows dialog; Presentation
/// only builds them. Faked in tests. Printing never touches the database.
/// </summary>
public interface IReciboPrinter
{
    Task<PrintOutcome> PrintAsync(IReadOnlyList<ReciboSheet> sheets, CancellationToken ct = default);
}

/// <summary>
/// What a recibo display needs to print: the reader, to read the payment again at print time (D13), the
/// printer, and the agency's letterhead as read from configuration, problems included.
/// </summary>
public sealed class ReciboPrinting
{
    public ReciboPrinting(IReciboReader reader, IReciboPrinter printer, AgencyDetailsRead agency)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(agency);

        Reader = reader;
        Printer = printer;
        Agency = agency;
    }

    public IReciboReader Reader { get; }

    public IReciboPrinter Printer { get; }

    public AgencyDetailsRead Agency { get; }
}
