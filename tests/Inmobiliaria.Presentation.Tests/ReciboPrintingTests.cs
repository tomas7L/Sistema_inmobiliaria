using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.CashDesk;
using Inmobiliaria.Presentation.Printing;
using Inmobiliaria.Presentation.Recibos;
using Inmobiliaria.Presentation.Tests.Fakes;
using Inmobiliaria.Presentation.Voiding;
using static Inmobiliaria.Presentation.Tests.CashDeskTestKit;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec tests 20 and 16 (nothing-printed half), Requirements 12, 14, 15, design section 9.3.</summary>
public class ReciboPrintingTests
{
    private static readonly AnnulmentRecord Annulment =
        new("Cobrado por error", "Nicolás", new DateTimeOffset(2026, 1, 16, 9, 5, 0, TimeSpan.FromHours(-3)));

    private sealed class Kit
    {
        public FakeReciboReader Reader { get; } = new();
        public FakeReciboPrinter Printer { get; } = new();
        public Guid CollectionId { get; } = Guid.NewGuid();

        public Kit()
        {
            Reader.Payments[CollectionId] = Issued(CollectionId, 31001);
        }

        public ReciboPrinting Printing(AgencyDetailsRead? agency = null) =>
            new(Reader, Printer, agency ?? AgencyDetails.Read(PrintingTestKit.AgencyValues));

        public ReciboViewModel Shown(AgencyDetailsRead? agency = null) =>
            new(Reader.Payments[CollectionId], Printing(agency));
    }

    /// <summary>Spec test 16, nothing-printed half: a commit sends nothing to the printer, and Print is offered.</summary>
    [Fact]
    public async Task ACommit_PrintsNothing_AndOffersPrint()
    {
        var kit = new CashDeskTestKit();
        var printer = new FakeReciboPrinter();
        var collectionId = Guid.NewGuid();
        kit.Reader.Payments[collectionId] = Issued(collectionId, 31001);
        kit.Desk.OnCommit = (_, _) => Task.FromResult(CollectionResult.Committed(collectionId, 31001));
        var desk = new CashDeskViewModel(kit.Desk, kit.Directory, kit.Adjustments, kit.Reader, kit.Clock,
            new ReciboPrinting(kit.Reader, printer, AgencyDetails.Read(PrintingTestKit.AgencyValues)));
        await desk.ChooseAccountAsync(Account());
        await TypeHandedAsync(desk, "598.260");
        await desk.ContinueCommand.ExecuteAsync(null);

        await desk.CommitCommand.ExecuteAsync(null);

        Assert.Empty(printer.Printed);
        Assert.True(desk.Recibo!.PrintBothCommand.CanExecute(null));
    }

    /// <summary>Spec test 20: a voiding sends nothing to the printer, and Print is offered for the annulled pair.</summary>
    [Fact]
    public async Task AVoiding_PrintsNothing_AndOffersPrint()
    {
        var kit = new Kit();
        var desk = new FakeCollectionDesk
        {
            OnPreviewVoid = (_, reason, role) => Task.FromResult(new VoidingPreview(
                Period, [new ReciboAnnulment(31001, reason), new ReciboAnnulment(31002, reason)], [], 0m, null, role)),
            OnVoid = _ =>
            {
                kit.Reader.Payments[kit.CollectionId] = kit.Reader.Payments[kit.CollectionId] with { Annulment = Annulment };
                return Task.FromResult(VoidingResult.Done());
            },
        };
        var voiding = new VoidingViewModel(desk, kit.Reader, new FakeClock(Today), UserRole.Admin, kit.Printing());
        await voiding.OpenAsync(kit.CollectionId);
        voiding.ReasonText = "Cobrado por error";
        await voiding.PreviewCommand.ExecuteAsync(null);

        await voiding.ConfirmCommand.ExecuteAsync(null);

        Assert.Empty(kit.Printer.Printed);
        Assert.True(voiding.Recibo!.IsAnnulled);
        Assert.True(voiding.Recibo.PrintBothCommand.CanExecute(null));
    }

    /// <summary>Design section 9.3: Print this recibo sends its one sheet; print both sends the payment's two.</summary>
    [Fact]
    public async Task PrintThisRecibo_SendsOneSheet_AndPrintBoth_SendsTwo()
    {
        var kit = new Kit();
        var recibo = kit.Shown();

        await recibo.PrintCommand.ExecuteAsync(recibo.Copies[1]);
        await recibo.PrintBothCommand.ExecuteAsync(null);

        Assert.Equal(["N° 31002"], kit.Printer.Printed[0].Select(s => s.NumberText));
        Assert.Equal(["N° 31001", "N° 31002"], kit.Printer.Printed[1].Select(s => s.NumberText));
        Assert.Equal("Enviado a la impresora.", recibo.PrintMessage);
    }

    /// <summary>
    /// Spec Requirement 15, D13: each Print reads the payment again first, so a pair voided while the screen
    /// was open prints ANULADO.
    /// </summary>
    [Fact]
    public async Task APairVoidedWhileOnScreen_PrintsAnulado()
    {
        var kit = new Kit();
        var recibo = kit.Shown();
        kit.Reader.Payments[kit.CollectionId] = kit.Reader.Payments[kit.CollectionId] with { Annulment = Annulment };

        await recibo.PrintBothCommand.ExecuteAsync(null);

        Assert.Equal([kit.CollectionId], kit.Reader.CollectionReads);
        Assert.All(kit.Printer.Printed.Single(), sheet => Assert.Equal("ANULADO", sheet.Original.Content.Annulment!.Word));
    }

    /// <summary>Spec test 20: cancelling the dialog changes nothing and calls no database port afterwards.</summary>
    [Fact]
    public async Task CancellingTheDialog_ChangesNothing()
    {
        var kit = new Kit();
        kit.Printer.Outcome = PrintOutcome.Cancelled;
        var recibo = kit.Shown();

        await recibo.PrintBothCommand.ExecuteAsync(null);

        Assert.Single(kit.Reader.CollectionReads);
        Assert.Null(recibo.PrintMessage);
        Assert.Null(recibo.Runner.ErrorMessage);
        Assert.True(recibo.PrintBothCommand.CanExecute(null));
    }

    /// <summary>Spec test 24c: missing agency data stops the print before any read or dialog, naming the item.</summary>
    [Fact]
    public async Task MissingAgencyData_StopsThePrint_NamingTheItem()
    {
        var kit = new Kit();
        var values = new Dictionary<string, string?>(PrintingTestKit.AgencyValues) { ["Phone"] = "" };
        var recibo = kit.Shown(AgencyDetails.Read(values));

        await recibo.PrintBothCommand.ExecuteAsync(null);

        Assert.Empty(kit.Printer.Printed);
        Assert.Empty(kit.Reader.CollectionReads);
        Assert.Contains("Agency:Phone", recibo.PrintMessage);
    }

    /// <summary>A display built without printing offers no Print.</summary>
    [Fact]
    public void WithoutPrinting_NoPrintIsOffered()
    {
        var recibo = new ReciboViewModel(Issued(Guid.NewGuid(), 31001));

        Assert.False(recibo.CanPrint);
        Assert.False(recibo.PrintBothCommand.CanExecute(null));
    }
}
