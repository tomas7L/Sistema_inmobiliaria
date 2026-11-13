using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Notices;
using Inmobiliaria.Presentation.Notices;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>notificaciones spec Requirements 1, 5 and 8, Tests 12, 16 and 17: the Avisos screen's view-models.</summary>
public class NoticesViewModelTests
{
    private static readonly DateOnly Today = new(2026, 12, 3);
    private static readonly MailboxCredentials Mailbox = new("avisos@example.com", "abcd efgh ijkl mnop");
    private const string Agency = "Del Lago Servicios Inmobiliarios";

    private static TenantNotice Notice(string shortId, string tenant, decimal total = 520_000m, decimal recargo = 20_000m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), shortId, tenant, "San Martín 45, Sunchales", $"{shortId}@example.com", Today,
            total, recargo, 12, TenantNotice.MonthOf(Today));

    private sealed class FakeNoticeDesk : ITenantNoticeDesk
    {
        public NoticeBoard Board { get; set; } = new([], [], []);

        public int Reads { get; private set; }

        public List<(IReadOnlyList<TenantNotice> Notices, MailboxCredentials Credentials, string AgencyName)> Sends { get; } = [];

        /// <summary>How each notice of a press ends; Sent unless named.</summary>
        public Func<TenantNotice, NoticeSendOutcome> Outcome { get; set; } = NoticeSendOutcome.Sent;

        public Exception? ReadFailure { get; set; }

        public Task<NoticeBoard> ReadAsync(CancellationToken ct = default)
        {
            Reads++;
            return ReadFailure is not null ? Task.FromException<NoticeBoard>(ReadFailure) : Task.FromResult(Board);
        }

        public Task<NoticeSendReport> SendAsync(
            IReadOnlyList<TenantNotice> notices, MailboxCredentials credentials, string agencyName, CancellationToken ct = default)
        {
            Sends.Add((notices.ToList(), credentials, agencyName));
            return Task.FromResult(new NoticeSendReport(notices.Select(Outcome).ToList()));
        }
    }

    private sealed class FakeMailboxStore : IMailboxCredentialStore
    {
        public MailboxCredentials? Stored { get; set; }

        public List<MailboxCredentials> Saved { get; } = [];

        public Exception? SaveFailure { get; set; }

        public Exception? ReadFailure { get; set; }

        public int Reads { get; private set; }

        public MailboxCredentials? Read()
        {
            Reads++;
            return ReadFailure is not null ? throw ReadFailure : Stored;
        }

        public void Save(MailboxCredentials credentials)
        {
            if (SaveFailure is not null)
            {
                throw SaveFailure;
            }

            Saved.Add(credentials);
            Stored = credentials;
        }
    }

    private static (NoticesViewModel Notices, FakeNoticeDesk Desk, FakeMailboxStore Store) Build(
        UserRole role = UserRole.Empleado, MailboxCredentials? stored = null, string? agencyName = Agency)
    {
        var desk = new FakeNoticeDesk();
        var store = new FakeMailboxStore { Stored = stored };
        return (new NoticesViewModel(desk, store, role, agencyName), desk, store);
    }

    /// <summary>Test 12: with no stored password the list still loads, and sending is disabled, saying why.</summary>
    [Fact]
    public async Task WithNoMailboxStored_TheListLoads_AndSendingIsDisabledWithTheReason()
    {
        var (notices, desk, _) = Build(stored: null);
        desk.Board = new([Notice("ab12cd34", "Pérez, María")], [new UnreachableTenant("ff00aa11", "Gómez, Luis", "Belgrano 10")], []);

        await notices.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["Pérez, María"], notices.Rows.Select(r => r.Tenant));
        Assert.Equal(["Gómez, Luis"], notices.NoEmail.Select(r => r.Tenant));
        Assert.False(notices.SendCommand.CanExecute(null));
        Assert.Equal(NoticesTexts.NoMailbox, notices.SendUnavailableReason);
    }

    /// <summary>
    /// A credential Windows cannot read is not called missing: the operator is told it could not be read (gentle-ai review,
    /// R4-unreadable-credential-reads-as-not-configured). A press reads the store once for itself, and once more when it reads
    /// the list again afterwards.
    /// </summary>
    [Fact]
    public async Task AnUnreadableMailbox_DisablesSendingSayingItCouldNotBeRead_AndAPressReadsTheStoreOnce()
    {
        var (notices, desk, store) = Build(stored: Mailbox);
        desk.Board = new([Notice("ab12cd34", "Pérez, María")], [], []);
        await notices.LoadCommand.ExecuteAsync(null);
        var readsBeforePress = store.Reads;

        await notices.SendCommand.ExecuteAsync(null);
        Assert.Equal(readsBeforePress + 2, store.Reads);

        store.ReadFailure = new InvalidOperationException("Credential Manager is unavailable");
        await notices.LoadCommand.ExecuteAsync(null);

        Assert.Equal(NoticesTexts.MailboxUnreadable, notices.SendUnavailableReason);
        Assert.False(notices.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task WithTheMailboxStored_SendingIsEnabled()
    {
        var (notices, desk, _) = Build(stored: Mailbox);
        desk.Board = new([Notice("ab12cd34", "Pérez, María")], [], []);

        await notices.LoadCommand.ExecuteAsync(null);

        Assert.Null(notices.SendUnavailableReason);
        Assert.True(notices.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task WithNoAgencyName_SendingIsDisabledNamingTheSetting()
    {
        var (notices, desk, _) = Build(stored: Mailbox, agencyName: null);
        desk.Board = new([Notice("ab12cd34", "Pérez, María")], [], []);

        await notices.LoadCommand.ExecuteAsync(null);

        Assert.False(notices.SendCommand.CanExecute(null));
        Assert.Equal(NoticesTexts.NoAgencyName, notices.SendUnavailableReason);
    }

    [Fact]
    public async Task WithNothingToSend_SendingIsDisabled_AndTheScreenSaysSo()
    {
        var (notices, _, _) = Build(stored: Mailbox);

        await notices.LoadCommand.ExecuteAsync(null);

        Assert.True(notices.IsEmpty);
        Assert.False(notices.SendCommand.CanExecute(null));
    }

    /// <summary>
    /// Test 17: after a press the screen reports how many were sent, how many already were, and each failure with its reason; a
    /// removed row is not sent; the list is read again afterwards, and the removed one is offered again (spec Requirement 1).
    /// </summary>
    [Fact]
    public async Task APress_ReportsSentAlreadySentAndFailed_ARemovedRowIsNotSent_AndTheListIsReadAgain()
    {
        var (notices, desk, _) = Build(stored: Mailbox);
        var first = Notice("aa000001", "Pérez, María");
        var second = Notice("aa000002", "Gómez, Luis");
        var third = Notice("aa000003", "Ruiz, Ana");
        var removed = Notice("aa000004", "Sosa, Juan");
        desk.Board = new([first, second, third, removed], [], []);
        desk.Outcome = n => n == second
            ? NoticeSendOutcome.Failed(n, "Gmail no aceptó el correo: 550 no such user")
            : n == third ? NoticeSendOutcome.AlreadySent(n) : NoticeSendOutcome.Sent(n);
        await notices.LoadCommand.ExecuteAsync(null);

        notices.RemoveCommand.Execute(notices.Rows.Single(r => r.Tenant == "Sosa, Juan"));
        await notices.SendCommand.ExecuteAsync(null);

        var send = Assert.Single(desk.Sends);
        Assert.Equal([first, second, third], send.Notices);
        Assert.Equal((Mailbox, Agency), (send.Credentials, send.AgencyName));
        Assert.Equal((1, 1), (notices.SentCount, notices.AlreadySentCount));
        Assert.Equal(["aa000002 (Gómez, Luis): Gmail no aceptó el correo: 550 no such user"], notices.Failures);
        Assert.Equal(NoticesTexts.Report(1, 1, 1), notices.ReportText);
        Assert.Equal(3, desk.Reads);
        Assert.Contains(notices.Rows, r => r.Tenant == "Sosa, Juan");
    }

    /// <summary>
    /// The list is read again just before sending, so a screen left open since yesterday sends today's figures and month,
    /// never the ones on screen (verifier advisory 6, Phase 2). A row no longer due is left out.
    /// </summary>
    [Fact]
    public async Task APress_SendsTheFiguresReadAtThePress_NotTheOnesOnScreen()
    {
        var (notices, desk, _) = Build(stored: Mailbox);
        var stale = Notice("aa000001", "Pérez, María", total: 500_000m);
        var paid = Notice("aa000002", "Gómez, Luis");
        desk.Board = new([stale, paid], [], []);
        await notices.LoadCommand.ExecuteAsync(null);

        var fresh = stale with { Total = 530_000m, FiguresOn = Today.AddDays(1) };
        desk.Board = new([fresh], [], []);
        await notices.SendCommand.ExecuteAsync(null);

        Assert.Equal([fresh], Assert.Single(desk.Sends).Notices);
    }

    /// <summary>
    /// A notice another PC sent between the read and the press is reported as already sent, and one no longer owed is reported
    /// apart, never dropped without a word (spec Requirements 4 and 5; Phase 3 verifier, B1).
    /// </summary>
    [Fact]
    public async Task APress_ReportsANoticeSentMeanwhileAsAlreadySent_AndOneNoLongerOwedApart()
    {
        var (notices, desk, _) = Build(stored: Mailbox);
        var sentElsewhere = Notice("aa000001", "Pérez, María");
        var paid = Notice("aa000002", "Gómez, Luis");
        var due = Notice("aa000003", "Ruiz, Ana");
        desk.Board = new([sentElsewhere, paid, due], [], []);
        await notices.LoadCommand.ExecuteAsync(null);

        desk.Board = new([due], [], [new SentNotice(sentElsewhere.ContractId, "aa000001", "Pérez, María", "maria", DateTimeOffset.UtcNow)]);
        await notices.SendCommand.ExecuteAsync(null);

        Assert.Equal([due], Assert.Single(desk.Sends).Notices);
        Assert.Equal((1, 1), (notices.SentCount, notices.AlreadySentCount));
        Assert.Empty(notices.Failures);
        Assert.Equal(NoticesTexts.Report(1, 1, 0) + " " + NoticesTexts.NoLongerOwed(["aa000002 (Gómez, Luis)"]), notices.ReportText);
    }

    /// <summary>
    /// Two contracts can share a short id: the one sent elsewhere is already sent, and the other, paid meanwhile, is still said
    /// apart, because a press tells them by contract, never by short id (second gentle-ai review, R3-sent-meanwhile-matched-by-short-id).
    /// </summary>
    [Fact]
    public async Task APress_TellsTwoContractsSharingAShortIdApart()
    {
        var (notices, desk, _) = Build(stored: Mailbox);
        var sentElsewhere = Notice("aa000001", "Pérez, María");
        var paid = Notice("aa000001", "Gómez, Luis");
        desk.Board = new([sentElsewhere, paid], [], []);
        await notices.LoadCommand.ExecuteAsync(null);

        desk.Board = new([], [], [new SentNotice(sentElsewhere.ContractId, "aa000001", "Pérez, María", "maria", DateTimeOffset.UtcNow)]);
        await notices.SendCommand.ExecuteAsync(null);

        Assert.Empty(Assert.Single(desk.Sends).Notices);
        Assert.Equal((0, 1), (notices.SentCount, notices.AlreadySentCount));
        Assert.Equal(NoticesTexts.Report(0, 1, 0) + " " + NoticesTexts.NoLongerOwed(["aa000001 (Gómez, Luis)"]), notices.ReportText);
    }

    /// <summary>A failed press says what to do in this screen's words, not the cash desk's (Phase 3 verifier, advisory 1).</summary>
    [Fact]
    public async Task AFailedPress_SaysToReadTheListAgain()
    {
        var (notices, desk, _) = Build(stored: Mailbox);
        desk.Board = new([Notice("aa000001", "Pérez, María")], [], []);
        await notices.LoadCommand.ExecuteAsync(null);

        desk.ReadFailure = new InvalidOperationException("connection refused");
        await notices.SendCommand.ExecuteAsync(null);

        Assert.Empty(desk.Sends);
        Assert.Equal(NoticesTexts.SendFailed, notices.Runner.ErrorMessage);
    }

    /// <summary>The credentials are read from the store at the press, so a mailbox set up after the screen opened is used.</summary>
    [Fact]
    public async Task APress_UsesTheMailboxStoredAtThePress()
    {
        var (notices, desk, store) = Build(stored: Mailbox);
        desk.Board = new([Notice("aa000001", "Pérez, María")], [], []);
        await notices.LoadCommand.ExecuteAsync(null);

        var replaced = new MailboxCredentials("otra@example.com", "qrst uvwx yzab cdef");
        store.Stored = replaced;
        await notices.SendCommand.ExecuteAsync(null);

        Assert.Same(replaced, Assert.Single(desk.Sends).Credentials);
    }

    [Fact]
    public async Task AFailedRead_ShowsOnlyItsMessage_AndNoEarlierRows()
    {
        var (notices, desk, _) = Build(stored: Mailbox);
        desk.Board = new([Notice("aa000001", "Pérez, María")], [], []);
        await notices.LoadCommand.ExecuteAsync(null);

        desk.ReadFailure = new InvalidOperationException("connection refused");
        await notices.LoadCommand.ExecuteAsync(null);

        Assert.Empty(notices.Rows);
        Assert.False(notices.IsEmpty);
        Assert.NotNull(notices.Runner.ErrorMessage);
        Assert.False(notices.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheAlreadySentList_NamesWhoSentItAndWhen()
    {
        var (notices, desk, _) = Build(stored: Mailbox);
        desk.Board = new([], [], [new SentNotice(Guid.NewGuid(), "aa000001", "Pérez, María", "nicolas", new DateTimeOffset(2026, 12, 2, 10, 30, 0, TimeSpan.FromHours(-3)))]);

        await notices.LoadCommand.ExecuteAsync(null);

        var sent = Assert.Single(notices.AlreadySent);
        Assert.Equal(("aa000001", "Pérez, María", "nicolas"), (sent.ShortId, sent.Tenant, sent.RecordedBy));
    }

    /// <summary>Test 16, the screen's half: only an Admin is offered the mailbox setup (spec Requirement 8, design D11).</summary>
    [Fact]
    public void OnlyAnAdmin_IsOfferedTheMailboxSetup()
    {
        Assert.NotNull(Build(UserRole.Admin).Notices.MailboxSetup);
        Assert.Null(Build(UserRole.Empleado).Notices.MailboxSetup);
    }

    [Fact]
    public async Task SettingUpTheMailbox_EnablesSending_WithoutReadingTheListAgain()
    {
        var (notices, desk, store) = Build(UserRole.Admin, stored: null);
        desk.Board = new([Notice("aa000001", "Pérez, María")], [], []);
        await notices.LoadCommand.ExecuteAsync(null);
        var setup = notices.MailboxSetup!;

        setup.Address = "avisos@example.com";
        setup.AppPassword = "abcd efgh ijkl mnop";
        await setup.SaveCommand.ExecuteAsync(null);

        Assert.Equal(("avisos@example.com", "abcd efgh ijkl mnop"), (store.Stored!.Address, store.Stored.AppPassword));
        Assert.True(notices.SendCommand.CanExecute(null));
        Assert.Equal(1, desk.Reads);
    }
}

/// <summary>The mailbox setup (design §4.2): never shows a stored password back, only whether one is stored and for whom.</summary>
public class MailboxSetupViewModelTests
{
    private sealed class Store : IMailboxCredentialStore
    {
        public MailboxCredentials? Stored { get; set; }

        public Exception? SaveFailure { get; set; }

        public MailboxCredentials? Read() => Stored;

        public void Save(MailboxCredentials credentials)
        {
            if (SaveFailure is not null)
            {
                throw SaveFailure;
            }

            Stored = credentials;
        }
    }

    [Fact]
    public void WithAMailboxStored_ItNamesTheAddress_AndNeverShowsThePassword()
    {
        var setup = new MailboxSetupViewModel(new Store { Stored = new("avisos@example.com", "abcd efgh ijkl mnop") });

        Assert.Equal(NoticesTexts.MailboxStored("avisos@example.com"), setup.StoredText);
        Assert.Equal("avisos@example.com", setup.Address);
        Assert.Equal(string.Empty, setup.AppPassword);
    }

    [Fact]
    public void WithNothingStored_ItSaysSo()
    {
        Assert.Equal(NoticesTexts.MailboxNotStored, new MailboxSetupViewModel(new Store()).StoredText);
    }

    [Fact]
    public async Task Saving_StoresBoth_ClearsThePasswordField_AndSaysForWhichAddress()
    {
        var store = new Store();
        var setup = new MailboxSetupViewModel(store) { Address = " avisos@example.com ", AppPassword = "abcd efgh ijkl mnop" };
        var saved = 0;
        setup.Saved += (_, _) => saved++;

        await setup.SaveCommand.ExecuteAsync(null);

        Assert.Equal(("avisos@example.com", "abcd efgh ijkl mnop"), (store.Stored!.Address, store.Stored.AppPassword));
        Assert.Equal(string.Empty, setup.AppPassword);
        Assert.Equal(NoticesTexts.MailboxStored("avisos@example.com"), setup.StoredText);
        Assert.Equal(1, saved);
    }

    [Theory]
    [InlineData("", "abcd efgh ijkl mnop")]
    [InlineData("not an address", "abcd efgh ijkl mnop")]
    [InlineData("avisos@example.com", "  ")]
    public void AMissingOrMalformedField_CannotBeSaved(string address, string password)
    {
        var setup = new MailboxSetupViewModel(new Store()) { Address = address, AppPassword = password };

        Assert.False(setup.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task AFailedSave_ShowsAMessageWithoutThePassword_AndStoresNothing()
    {
        var store = new Store { SaveFailure = new InvalidOperationException("Credential Manager refused abcd efgh ijkl mnop") };
        var log = new RecordingErrorLog();
        var setup = new MailboxSetupViewModel(store, log) { Address = "avisos@example.com", AppPassword = "abcd efgh ijkl mnop" };

        await setup.SaveCommand.ExecuteAsync(null);

        Assert.Null(store.Stored);
        Assert.Equal(NoticesTexts.SaveFailed, setup.Runner.ErrorMessage);
        Assert.Equal(NoticesTexts.MailboxNotStored, setup.StoredText);

        // spec Requirement 6: the error log never holds the password, whatever the store's exception quoted.
        var entry = Assert.Single(log.Entries);
        Assert.Equal("Credential Manager refused ***", entry.Exception.Message);
        Assert.Null(entry.Exception.InnerException);
    }
}
