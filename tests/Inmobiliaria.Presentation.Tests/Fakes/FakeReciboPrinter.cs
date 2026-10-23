using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>A printer that records what it was asked to print and answers as the test says.</summary>
internal sealed class FakeReciboPrinter : IReciboPrinter
{
    public PrintOutcome Outcome { get; set; } = PrintOutcome.Printed;

    public List<IReadOnlyList<ReciboSheet>> Printed { get; } = [];

    public Task<PrintOutcome> PrintAsync(IReadOnlyList<ReciboSheet> sheets, CancellationToken ct = default)
    {
        Printed.Add(sheets);
        return Task.FromResult(Outcome);
    }
}
