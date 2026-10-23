using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Presentation.Recibos;

/// <summary>
/// The recent collections (spec Requirement 13, design D19): the newest 50, each with both numbers, the tenant,
/// the period, the received and recorded dates and whether it is annulled; a pair found by either printed
/// number; an entry opened as issued, or handed to the voiding screen. Both roles use it.
/// </summary>
public sealed partial class RecentCollectionsViewModel : ObservableObject
{
    /// <summary>How many collections the list holds (design D19); older ones are found by number.</summary>
    public const int Limit = 50;

    private readonly IReciboReader _reader;
    private readonly Func<Guid, Task> _onVoid;
    private readonly ReciboPrinting? _printing;

    /// <param name="reader">The recibo reader.</param>
    /// <param name="onVoid">Hands a collection to the voiding screen.</param>
    /// <param name="canVoid">False when there is no voiding screen (invalid recargo terms, D16).</param>
    /// <param name="printing">What the recibo display prints with, or null for no Print.</param>
    public RecentCollectionsViewModel(
        IReciboReader reader, Func<Guid, Task> onVoid, bool canVoid = true, ReciboPrinting? printing = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(onVoid);

        _reader = reader;
        _onVoid = onVoid;
        CanVoid = canVoid;
        _printing = printing;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !Runner.IsBusy);
        FindCommand = new AsyncRelayCommand(FindAsync, () => !Runner.IsBusy);
        OpenCommand = new AsyncRelayCommand<RecentEntry>(OpenAsync, entry => entry is not null && !Runner.IsBusy);
        VoidCommand = new AsyncRelayCommand<RecentEntry>(
            entry => entry is null ? Task.CompletedTask : _onVoid(entry.CollectionId),
            entry => CanVoid && entry is { IsAnnulled: false });
        Runner.PropertyChanged += OnRunnerChanged;
    }

    public OperationRunner Runner { get; } = new();

    /// <summary>Whether a voiding screen exists to hand entries to.</summary>
    public bool CanVoid { get; }

    public ObservableCollection<RecentEntry> Entries { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand FindCommand { get; }

    public IAsyncRelayCommand<RecentEntry> OpenCommand { get; }

    /// <summary>Hands a live entry to the voiding screen; an annulled one offers no voiding.</summary>
    public IAsyncRelayCommand<RecentEntry> VoidCommand { get; }

    [ObservableProperty]
    private string _numberText = string.Empty;

    [ObservableProperty]
    private string? _findMessage;

    [ObservableProperty]
    private ReciboViewModel? _recibo;

    private async Task LoadAsync()
    {
        var outcome = await Runner.RunAsync(() => _reader.RecentAsync(Limit));
        if (!outcome.Completed)
        {
            return;
        }

        Entries.Clear();
        foreach (var entry in outcome.Value!)
        {
            Entries.Add(new RecentEntry(entry));
        }
    }

    private async Task FindAsync()
    {
        FindMessage = null;
        if (!ReciboSearch.TryParse(NumberText, out var number))
        {
            Recibo = null;
            FindMessage = ReciboSearch.TypeANumber;
            return;
        }

        var outcome = await Runner.RunAsync(() => _reader.FindByNumberAsync(number));
        if (!outcome.Completed)
        {
            return;
        }

        Recibo = outcome.Value is { } payment ? new ReciboViewModel(payment, _printing) : null;
        FindMessage = outcome.Value is null ? ReciboSearch.NotFound(number) : null;
    }

    private async Task OpenAsync(RecentEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        FindMessage = null;
        var outcome = await Runner.RunAsync(() => _reader.GetByCollectionAsync(entry.CollectionId));
        if (outcome.Completed)
        {
            Recibo = outcome.Value is { } payment ? new ReciboViewModel(payment, _printing) : null;
        }
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            FindCommand.NotifyCanExecuteChanged();
            OpenCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>One line of the recent collections.</summary>
public sealed class RecentEntry
{
    public RecentEntry(RecentCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        CollectionId = collection.CollectionId;
        NumbersText = $"N° {collection.LocatarioNumber} / {collection.PropietarioNumber}";
        Tenant = collection.Tenant;
        PeriodText = Formats.Period(collection.Period);
        ReceivedOnText = Formats.Date(collection.ReceivedOn);
        // The reader hands the recorded time over already in the agency's zone.
        RecordedText = collection.RecordedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        IsAnnulled = collection.IsAnnulled;
        AnnulledText = collection.IsAnnulled ? "ANULADO" : string.Empty;
    }

    public Guid CollectionId { get; }

    public string NumbersText { get; }

    public string Tenant { get; }

    public string PeriodText { get; }

    public string ReceivedOnText { get; }

    public string RecordedText { get; }

    public bool IsAnnulled { get; }

    public string AnnulledText { get; }
}
