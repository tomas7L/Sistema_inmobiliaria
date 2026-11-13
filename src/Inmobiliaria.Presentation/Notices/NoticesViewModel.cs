using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Notices;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Notices;

/// <summary>
/// The Avisos screen (notificaciones spec Requirements 1, 5 and 8, design §4.1): today's notices, the tenants with no email,
/// and those already sent this month. Both roles use it. The list is read when the screen opens and when the operator asks,
/// like Mora's. A row can be removed before a press, which only leaves it out of that press.
/// </summary>
/// <remarks>
/// A press reads the list again first and sends only what is still due today, in today's figures, so a screen left open
/// since yesterday never sends yesterday's (Phase 2 verifier advisory 6). It reads the mailbox from the store at that moment,
/// and it cannot be cancelled once started: what Gmail accepts is recorded, and the report covers every notice (Phase 2 review,
/// R3-cancel-mid-batch-drops-report).
/// </remarks>
public sealed partial class NoticesViewModel : ObservableObject
{
    private readonly ITenantNoticeDesk _desk;
    private readonly IMailboxCredentialStore _store;
    private readonly string? _agencyName;

    /// <param name="desk">Today's notices and sending them; built only with valid recargo terms (spec Requirement 8).</param>
    /// <param name="store">This PC's mailbox (design D9).</param>
    /// <param name="role">Decides whether the mailbox setup is offered (spec Decision 18).</param>
    /// <param name="agencyName">The name the emails are signed with, or null when it is not configured.</param>
    public NoticesViewModel(ITenantNoticeDesk desk, IMailboxCredentialStore store, UserRole role, string? agencyName)
    {
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(store);

        _desk = desk;
        _store = store;
        _agencyName = string.IsNullOrWhiteSpace(agencyName) ? null : agencyName.Trim();
        LoadCommand = new AsyncRelayCommand(() => LoadAsync(clearReport: true), () => !Runner.IsBusy);
        SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
        RemoveCommand = new RelayCommand<NoticeRow>(Remove, row => row is not null && !Runner.IsBusy);
        Runner.PropertyChanged += OnRunnerChanged;
        Rows.CollectionChanged += (_, _) => SendCommand.NotifyCanExecuteChanged();

        // Only an Admin is offered it; the database never holds the password, so this is the only gate (spec Requirement 8).
        if (role == UserRole.Admin)
        {
            MailboxSetup = new MailboxSetupViewModel(store);
            MailboxSetup.Saved += (_, _) => _ = RefreshSendAvailability();
        }
    }

    /// <summary>Its <c>IsBusy</c> is the loading and sending state; its <c>ErrorMessage</c> a failed read or press.</summary>
    public OperationRunner Runner { get; } = new();

    /// <summary>The notices a press would send.</summary>
    public ObservableCollection<NoticeRow> Rows { get; } = [];

    /// <summary>Contracts that owe but whose tenant has no usable email, so the operator knows whom to call (spec Decision 17).</summary>
    public ObservableCollection<UnreachableTenant> NoEmail { get; } = [];

    public ObservableCollection<SentNoticeRow> AlreadySent { get; } = [];

    /// <summary>Each failure of the last press, with its contract, tenant and reason.</summary>
    public ObservableCollection<string> Failures { get; } = [];

    /// <summary>The mailbox setup, for an Admin; null for an Empleado.</summary>
    public MailboxSetupViewModel? MailboxSetup { get; }

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SendCommand { get; }

    /// <summary>Leaves a row out of the next press only; reading the list again offers it again.</summary>
    public IRelayCommand<NoticeRow> RemoveCommand { get; }

    /// <summary>True after a read that found nothing to send.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Why sending is disabled even with rows to send, or null when it is not (spec Requirement 5).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _sendUnavailableReason;

    [ObservableProperty]
    private int _sentCount;

    [ObservableProperty]
    private int _alreadySentCount;

    /// <summary>The counts of the last press, or null before one.</summary>
    [ObservableProperty]
    private string? _reportText;

    private bool CanSend() => !Runner.IsBusy && Rows.Count > 0 && SendUnavailableReason is null;

    private async Task LoadAsync(bool clearReport)
    {
        if (clearReport)
        {
            ClearReport();
        }

        _ = RefreshSendAvailability();
        var outcome = await Runner.RunAsync(() => _desk.ReadAsync());

        Rows.Clear();
        NoEmail.Clear();
        AlreadySent.Clear();
        if (!outcome.Completed)
        {
            // A failed read shows only its message: never "nothing to send", nor an earlier read's rows as if current.
            IsEmpty = false;
            return;
        }

        var board = outcome.Value!;
        foreach (var notice in board.ToSend)
        {
            Rows.Add(new NoticeRow(notice));
        }

        foreach (var tenant in board.NoEmail)
        {
            NoEmail.Add(tenant);
        }

        foreach (var sent in board.AlreadySent)
        {
            AlreadySent.Add(new SentNoticeRow(sent));
        }

        IsEmpty = Rows.Count == 0;
    }

    private async Task SendAsync()
    {
        ClearReport();
        var credentials = RefreshSendAvailability();
        if (credentials is null || _agencyName is null)
        {
            return;
        }

        var chosen = Rows.Select(row => row.Notice).ToList();
        var outcome = await Runner.RunAsync(
            async () =>
            {
                var current = await _desk.ReadAsync();
                var stillDue = current.ToSend.ToDictionary(notice => notice.ContractId);
                var sentShortIds = current.AlreadySent.Select(sent => sent.ContractShortId).ToHashSet(StringComparer.Ordinal);

                var notices = chosen.Where(row => stillDue.ContainsKey(row.ContractId)).Select(row => stillDue[row.ContractId]).ToList();
                var left = chosen.Where(row => !stillDue.ContainsKey(row.ContractId)).ToList();

                // A row another PC sent meanwhile is already sent; one that no longer owes is said apart, never dropped silently
                // (spec Requirements 4 and 5).
                var sentMeanwhile = left.Count(row => sentShortIds.Contains(row.ContractShortId));
                var noLongerOwed = left.Where(row => !sentShortIds.Contains(row.ContractShortId)).ToList();
                var report = await _desk.SendAsync(notices, credentials, _agencyName);
                return (Report: report, SentMeanwhile: sentMeanwhile, NoLongerOwed: noLongerOwed);
            },
            OperationKind.Commit);

        if (!outcome.Completed)
        {
            // The cash desk's commit message speaks of a payment; this screen says what to do here: read the list again, which
            // shows what went out as already sent.
            Runner.ErrorMessage = NoticesTexts.SendFailed;
            return;
        }

        var (report, sentMeanwhile, noLongerOwed) = outcome.Value;
        SentCount = report.SentCount;
        AlreadySentCount = report.AlreadySentCount + sentMeanwhile;
        foreach (var failure in report.Failures)
        {
            Failures.Add(NoticesTexts.Failure(failure.Notice.ContractShortId, failure.Notice.Tenant, failure.Reason!));
        }

        ReportText = NoticesTexts.Report(SentCount, AlreadySentCount, report.Failures.Count)
            + (noLongerOwed.Count == 0
                ? string.Empty
                : " " + NoticesTexts.NoLongerOwed(noLongerOwed.Select(row => NoticesTexts.Row(row.ContractShortId, row.Tenant))));

        // What went out is now among the sent, and what failed or was removed is offered again (spec Requirement 5).
        await LoadAsync(clearReport: false);
    }

    private void Remove(NoticeRow? row)
    {
        if (row is not null)
        {
            Rows.Remove(row);
        }
    }

    /// <summary>
    /// Says whether sending is possible, and returns the stored mailbox when it is. Reads the store once; a credential Windows
    /// cannot read is said apart from a missing one (gentle-ai review, R4-unreadable-credential-reads-as-not-configured).
    /// </summary>
    private MailboxCredentials? RefreshSendAvailability()
    {
        if (_agencyName is null)
        {
            SendUnavailableReason = NoticesTexts.NoAgencyName;
            return null;
        }

        try
        {
            var credentials = _store.Read();
            SendUnavailableReason = credentials is null ? NoticesTexts.NoMailbox : null;
            return credentials;
        }
        catch (Exception exception)
        {
            OperationRunner.DefaultErrorLog.Record("Mailbox", exception);
            SendUnavailableReason = NoticesTexts.MailboxUnreadable;
            return null;
        }
    }

    private void ClearReport()
    {
        SentCount = 0;
        AlreadySentCount = 0;
        Failures.Clear();
        ReportText = null;
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            LoadCommand.NotifyCanExecuteChanged();
            SendCommand.NotifyCanExecuteChanged();
            RemoveCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>One notice as the screen shows it. The total already includes the recargo, so the two are never added.</summary>
public sealed class NoticeRow
{
    public NoticeRow(TenantNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        Notice = notice;
    }

    public TenantNotice Notice { get; }

    public string ShortId => Notice.ContractShortId;

    public string Tenant => Notice.Tenant;

    public string Units => Notice.Units;

    public string Recipient => Notice.Recipient;

    public string TotalText => Formats.Money(Notice.Total);

    public string RecargoText => Formats.Money(Notice.Recargo);

    public int DaysOverdue => Notice.DaysOverdue;
}

/// <summary>A notice already sent this month, with who sent it and when (spec Requirement 1).</summary>
public sealed class SentNoticeRow
{
    public SentNoticeRow(SentNotice sent)
    {
        ArgumentNullException.ThrowIfNull(sent);

        ShortId = sent.ContractShortId;
        Tenant = sent.Tenant;
        RecordedBy = sent.RecordedBy;
        RecordedAtText = sent.RecordedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    public string ShortId { get; }

    public string Tenant { get; }

    public string RecordedBy { get; }

    public string RecordedAtText { get; }
}
