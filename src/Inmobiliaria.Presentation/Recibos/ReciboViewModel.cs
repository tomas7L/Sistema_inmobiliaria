using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Presentation.Recibos;

/// <summary>
/// A payment's two recibos on screen, exactly as issued (spec Requirement 13): what the reader returned,
/// formatted and nothing recomputed. An annulled pair also says when, by whom and why, and its lines stay
/// readable. Print is offered, never performed by itself (Requirements 12, 14): each print reads the payment
/// again first, so a pair voided while on screen prints ANULADO (D13).
/// </summary>
public sealed partial class ReciboViewModel : ObservableObject
{
    private readonly ReciboPrinting? _printing;

    public ReciboViewModel(IssuedPayment payment, ReciboPrinting? printing = null)
    {
        ArgumentNullException.ThrowIfNull(payment);

        Payment = payment;
        _printing = printing;
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

        PrintCommand = new AsyncRelayCommand<ReciboCopyView>(
            copy => copy is null ? Task.CompletedTask : PrintAsync(copy.Copy), copy => copy is not null && CanPrintNow());
        PrintBothCommand = new AsyncRelayCommand(() => PrintAsync(null), CanPrintNow);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public IssuedPayment Payment { get; }

    public OperationRunner Runner { get; } = new();

    /// <summary>Locatario first, then propietario.</summary>
    public IReadOnlyList<ReciboCopyView> Copies { get; }

    public bool IsAnnulled => Payment.IsAnnulled;

    /// <summary>When, by whom and why the pair was annulled; null for a live payment.</summary>
    public string? AnnulmentText { get; }

    /// <summary>Whether this display can print at all.</summary>
    public bool CanPrint => _printing is not null;

    /// <summary>Prints the one recibo given.</summary>
    public IAsyncRelayCommand<ReciboCopyView> PrintCommand { get; }

    /// <summary>Prints both recibos of the payment: two sheets.</summary>
    public IAsyncRelayCommand PrintBothCommand { get; }

    /// <summary>What the last print said; null after a cancelled dialog.</summary>
    [ObservableProperty]
    private string? _printMessage;

    private bool CanPrintNow() => CanPrint && !Runner.IsBusy;

    private async Task PrintAsync(ReciboCopy? copy)
    {
        if (_printing is null)
        {
            return;
        }

        PrintMessage = null;

        // Missing letterhead stops before anything is read or any dialog opens (Decision 37).
        if (_printing.Agency.Details is not { } agency)
        {
            PrintMessage = _printing.Agency.Problem;
            return;
        }

        var read = await Runner.RunAsync(() => _printing.Reader.GetByCollectionAsync(Payment.CollectionId));
        if (!read.Completed)
        {
            return;
        }

        if (read.Value is not { } fresh)
        {
            PrintMessage = "No se encontró el cobro para imprimir.";
            return;
        }

        IReadOnlyList<ReciboSheet> sheets = copy is { } one
            ? [ReciboSheetBuilder.Build(fresh, one, agency)]
            : ReciboSheetBuilder.BuildBoth(fresh, agency);

        var printed = await Runner.RunAsync(() => _printing.Printer.PrintAsync(sheets));
        if (printed is { Completed: true, Value: PrintOutcome.Printed })
        {
            PrintMessage = "Enviado a la impresora.";
        }
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            PrintCommand.NotifyCanExecuteChanged();
            PrintBothCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>One recibo as the operator reads it.</summary>
public sealed class ReciboCopyView
{
    public ReciboCopyView(IssuedRecibo issued)
    {
        ArgumentNullException.ThrowIfNull(issued);

        var recibo = issued.Recibo;
        ReciboId = issued.ReciboId;
        Copy = recibo.Copy;
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

    public ReciboCopy Copy { get; }

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
