using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Mora;

/// <summary>
/// The Mora screen (mora spec Requirements 6 and 7, design §4.1): every account in mora as of today, worst first, each
/// naming who owes and both figures apart, and a row opened at the cash desk. Both roles use it. The list is read when
/// the screen opens and when the operator asks, never on a timer (spec Decision 13), and every read replaces it whole.
/// The order is the query's (design D4); this keeps it.
/// </summary>
public sealed partial class MoraViewModel : ObservableObject
{
    private readonly IMoraWorklistQuery _query;
    private readonly RecargoTerms _terms;
    private readonly IAgencyClock _clock;
    private readonly IAccountDirectory _directory;
    private readonly Func<AccountListing, Task> _openAtDesk;

    /// <param name="query">The worklist.</param>
    /// <param name="terms">The recargo terms the desk was built with; there is no Mora without valid ones (design D6).</param>
    /// <param name="clock">Today, the only day the list is asked about (spec Decision 9).</param>
    /// <param name="directory">Reads the listing a row opens the desk with (design D7).</param>
    /// <param name="openAtDesk">Shows the cash desk on that listing.</param>
    public MoraViewModel(
        IMoraWorklistQuery query,
        RecargoTerms terms,
        IAgencyClock clock,
        IAccountDirectory directory,
        Func<AccountListing, Task> openAtDesk)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(openAtDesk);

        _query = query;
        _terms = terms;
        _clock = clock;
        _directory = directory;
        _openAtDesk = openAtDesk;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !Runner.IsBusy);
        OpenCommand = new AsyncRelayCommand<MoraRow>(OpenAsync, row => row is not null && !Runner.IsBusy);
        Runner.PropertyChanged += OnRunnerChanged;
    }

    /// <summary>Its <c>IsBusy</c> is the loading state (spec Finding F5); its <c>ErrorMessage</c> a failed read.</summary>
    public OperationRunner Runner { get; } = new();

    public ObservableCollection<MoraRow> Rows { get; } = [];

    /// <summary>What the figures cover and what they never do (spec Requirement 6).</summary>
    public string CoverageText => MoraTexts.Coverage;

    /// <summary>Reads the list again; the screen runs it when it opens and the operator when they ask.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Opens a row's account at the cash desk (spec Requirement 7).</summary>
    public IAsyncRelayCommand<MoraRow> OpenCommand { get; }

    /// <summary>True after a read that found nobody in mora: the screen says so in words (spec Decision 12).</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Set when a row's account can no longer be opened at the desk.</summary>
    [ObservableProperty]
    private string? _openMessage;

    private async Task LoadAsync()
    {
        OpenMessage = null;
        var outcome = await Runner.RunAsync(() => _query.GetAccountsInMoraAsync(_clock.Today, _terms));
        if (!outcome.Completed)
        {
            // A failed read shows only its message: never "nobody in mora", nor an earlier read's figures as if current
            // (spec Decision 12).
            Rows.Clear();
            IsEmpty = false;
            return;
        }

        Rows.Clear();
        foreach (var account in outcome.Value!)
        {
            Rows.Add(new MoraRow(account));
        }

        IsEmpty = Rows.Count == 0;
    }

    private async Task OpenAsync(MoraRow? row)
    {
        if (row is null)
        {
            return;
        }

        OpenMessage = null;

        // Reading the listing and opening the desk run as one operation, so a failure in either becomes the screen's
        // message instead of escaping the command.
        var outcome = await Runner.RunAsync(async () =>
        {
            var listing = await _directory.FindAsync(row.AccountId);
            if (listing is not null)
            {
                await _openAtDesk(listing);
            }

            return listing is not null;
        });

        if (outcome.Completed && !outcome.Value)
        {
            OpenMessage = MoraTexts.AccountGone;
        }
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            OpenCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>
/// One account in mora as the screen shows it. Amount owed already includes the recargo, so the two are shown apart and
/// never added (spec Requirement 6).
/// </summary>
public sealed class MoraRow
{
    public MoraRow(MoraAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        Account = account;
    }

    public MoraAccount Account { get; }

    public Guid AccountId => Account.AccountId;

    public string Tenants => Account.Tenants;

    public string Units => Account.Units;

    public string ShortId => Account.ContractShortId;

    public string StatusText => Formats.Status(Account.Status);

    public int DaysOverdue => Account.DaysOverdue;

    public string OldestDueText => Formats.Date(Account.OldestUnpaidDueDate);

    public string AmountOwedText => Formats.Money(Account.AmountOwed);

    public string RecargoText => Formats.Money(Account.RecargoAccrued);

    /// <summary>The day a novación froze the recargo, or null when it still runs.</summary>
    public string? FrozenText => Account.RecargoFrozenOn is { } frozenOn ? MoraTexts.FrozenOn(Formats.Date(frozenOn)) : null;
}
