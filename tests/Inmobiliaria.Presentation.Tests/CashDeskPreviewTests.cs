using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.CashDesk;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Tests.Fakes;
using static Inmobiliaria.Presentation.Tests.CashDeskTestKit;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec tests 10 to 14 and 13a, design sections 7.1 and 7.3, D8, D9, D21, D23.</summary>
public class CashDeskPreviewTests
{
    /// <summary>Spec test 10: choosing an account previews it with a handed amount of 0.</summary>
    [Fact]
    public async Task ChoosingAnAccount_PreviewsItWithAHandedAmountOfZero()
    {
        var kit = new CashDeskTestKit();
        var account = Account();

        var desk = await kit.ChosenAsync(account);

        var request = Assert.Single(kit.PreviewRequests);
        Assert.Equal(new CollectionRequest(account.AccountId, null, null, 0m), request);
        Assert.Equal(DeskState.Owed, desk.State);
        Assert.Equal("598.260", desk.LedgerFigureText);
        Assert.Equal("01/2026", desk.PeriodText);
    }

    /// <summary>Spec test 10: changing the period, the received date or a closing line previews again.</summary>
    [Fact]
    public async Task ChangingAnInput_PreviewsAgain()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();

        desk.NamedPeriod = new DateTime(2025, 12, 1);
        await desk.PreviewInFlight;
        desk.ReceivedOnInput = new DateTime(2026, 1, 10);
        await desk.PreviewInFlight;
        desk.TasaMunicipalText = "1.200";
        await desk.PreviewInFlight;
        desk.SeguroText = "4000";
        await desk.PreviewInFlight;
        desk.OtrosConceptosText = "-500";
        await desk.PreviewInFlight;

        Assert.Equal(6, kit.PreviewRequests.Count);
        Assert.Equal(
            new CollectionRequest(kit.PreviewRequests[0].AccountId, new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 10), 0m, 1_200m, 4_000m, -500m),
            kit.PreviewRequests[^1]);
    }

    /// <summary>
    /// Spec test 11, D21: the received date is sent as null unless typed, and the screen shows the preview's,
    /// which comes from the agency clock, not the machine's.
    /// </summary>
    [Fact]
    public async Task TheReceivedDate_IsSentAsNullUnlessTyped_AndShownFromThePreview()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();

        Assert.Null(kit.PreviewRequests[0].ReceivedOn);
        Assert.Equal("15/01/2026", desk.ReceivedOnText);

        desk.ReceivedOnInput = new DateTime(2026, 1, 12);
        await desk.PreviewInFlight;

        Assert.Equal(new DateOnly(2026, 1, 12), kit.PreviewRequests[^1].ReceivedOn);
        Assert.Equal("12/01/2026", desk.ReceivedOnText);
    }

    /// <summary>Spec test 12: AmountNotTotal at the owed step shows figures and no error.</summary>
    [Fact]
    public async Task AmountNotTotal_AtTheOwedStep_ShowsFiguresAndNoError()
    {
        var desk = await new CashDeskTestKit().ChosenAsync();

        Assert.Null(desk.RefusalMessage);
        Assert.Null(desk.Runner.ErrorMessage);
        Assert.Equal(["Alquiler", "Recargo por mora"], desk.LocatarioLines.Select(l => l.Label));
        Assert.False(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>Spec test 12: every other refusal is shown in its operator text and keeps the operator from the summary.</summary>
    [Theory]
    [InlineData(CollectionRefusal.NothingPayable)]
    [InlineData(CollectionRefusal.PeriodAlreadySettled)]
    [InlineData(CollectionRefusal.RateNeverEntered)]
    [InlineData(CollectionRefusal.AccountFrozen)]
    [InlineData(CollectionRefusal.ReceivedOnInFuture)]
    [InlineData(CollectionRefusal.NegativeTasaMunicipal)]
    public async Task ABlockingRefusal_IsShown_AndKeepsTheOperatorFromTheSummary(CollectionRefusal refusal)
    {
        var kit = new CashDeskTestKit();
        kit.Desk.OnPreview = request => Task.FromResult(PreviewFor(request, Ledger, Today) with { Refusal = refusal });
        var desk = await kit.ChosenAsync();

        await TypeHandedAsync(desk, "598.260");

        Assert.Equal(refusal.OperatorText(), desk.RefusalMessage);
        Assert.False(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>
    /// Spec test 13, D8: an unset starting number blocks before any summary, with the text naming the
    /// starting recibo number and where an Admin sets it, never a contract's cutover date.
    /// </summary>
    [Fact]
    public async Task AnUnsetStartingNumber_BlocksBeforeTheSummary_NamingTheStartingNumber()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync(Account(new AccountReadiness(true, false, false)));

        await TypeHandedAsync(desk, "598.260");

        Assert.Equal(CollectionRefusal.CutoverNotSet.OperatorText(), desk.ReadinessBlock);
        Assert.Contains("número inicial de recibo", desk.ReadinessBlock);
        Assert.Contains("Puesta en marcha", desk.ReadinessBlock);
        Assert.DoesNotContain("fecha de corte", desk.ReadinessBlock, StringComparison.OrdinalIgnoreCase);
        Assert.False(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>Spec test 13a, D9: a due adjustment missing indices warns, naming each index and period, and never blocks.</summary>
    [Fact]
    public async Task ADueAdjustmentMissingIndices_WarnsNamingEach_AndDoesNotBlock()
    {
        var kit = new CashDeskTestKit();
        var contractId = Guid.NewGuid();
        kit.Adjustments.Due =
        [
            Due(contractId,
                new MissingIndexValue(Guid.NewGuid(), "ICL", new IndexPeriod(2025, 12)),
                new MissingIndexValue(Guid.NewGuid(), "IPC", new IndexPeriod(2025, 11))),
            Due(Guid.NewGuid(), new MissingIndexValue(Guid.NewGuid(), "CAC", new IndexPeriod(2025, 12))),
        ];
        var desk = await kit.ChosenAsync(Account(contractId: contractId));

        Assert.Equal(
            [
                "Hay un ajuste de alquiler pendiente: falta el índice ICL de 12/2025.",
                "Hay un ajuste de alquiler pendiente: falta el índice IPC de 11/2025.",
            ],
            desk.Warnings);

        await TypeHandedAsync(desk, "598.260");
        Assert.True(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>Spec test 13a: a due adjustment with every index says it is ready to confirm; none shows nothing.</summary>
    [Fact]
    public async Task AReadyAdjustment_SaysSo_AndNoneShowsNothing()
    {
        var kit = new CashDeskTestKit();
        var contractId = Guid.NewGuid();
        kit.Adjustments.Due = [Due(contractId)];

        var withAdjustment = await kit.ChosenAsync(Account(contractId: contractId));
        var without = await kit.ChosenAsync(Account());

        Assert.Equal(["Hay un ajuste de alquiler listo para confirmar."], withAdjustment.Warnings);
        Assert.Empty(without.Warnings);
    }

    /// <summary>D9: the adjustments are asked for once per account selection, as of the agency's today.</summary>
    [Fact]
    public async Task Adjustments_AreAskedOncePerSelection_AsOfTheAgencysToday()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();

        desk.SeguroText = "100";
        await desk.PreviewInFlight;
        await TypeHandedAsync(desk, "598.260");

        Assert.Equal([Today], kit.Adjustments.Calls);
    }

    /// <summary>The 0% administration rate warns from the preview and does not block.</summary>
    [Fact]
    public async Task AZeroRate_Warns_AndDoesNotBlock()
    {
        var kit = new CashDeskTestKit();
        kit.Desk.OnPreview = request => Task.FromResult(
            PreviewFor(request, Ledger, Today) with { Warnings = [CollectionWarning.ZeroAdministrationRate] });
        var desk = await kit.ChosenAsync();

        await TypeHandedAsync(desk, "598.260");

        Assert.Equal([DeskTexts.ZeroAdministrationRate], desk.Warnings);
        Assert.True(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>
    /// Spec test 14: the handed amount starts empty; the ledger figure and the recibo totals carry different
    /// names; the ledger figure is accepted and the recibo total is refused.
    /// </summary>
    [Fact]
    public async Task TheLedgerFigureIsAccepted_AndTheReciboTotalIsRefused()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();

        Assert.Equal(string.Empty, desk.HandedAmountText);
        Assert.NotEqual(DeskTexts.LedgerFigureLabel, DeskTexts.LocatarioTotalLabel);
        Assert.Contains("saldo del período", DeskTexts.HandedAmountLabel, StringComparison.OrdinalIgnoreCase);

        desk.SeguroText = "4.000";
        await desk.PreviewInFlight;
        Assert.Equal(("598.260", "602.260"), (desk.LedgerFigureText, desk.LocatarioTotalText));

        await TypeHandedAsync(desk, "602.260");
        Assert.Equal(CollectionRefusal.AmountNotTotal.OperatorText(), desk.RefusalMessage);
        Assert.False(desk.ContinueCommand.CanExecute(null));

        await TypeHandedAsync(desk, "598.260");
        Assert.Null(desk.RefusalMessage);
        Assert.Equal(DeskState.Ready, desk.State);
        Assert.True(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>Spec test 14: a fractional seguro is sent as typed and shown as the domain truncated it.</summary>
    [Fact]
    public async Task AFractionalSeguro_IsShownTruncated()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();

        desk.SeguroText = "1250,80";
        await desk.PreviewInFlight;

        Assert.Equal(1_250.80m, kit.PreviewRequests[^1].Seguro);
        Assert.Equal("1.250", desk.LocatarioLines.Single(l => l.Label == "Seguro").AmountText);
    }

    /// <summary>A value that is not a number is named, and no preview is requested for it.</summary>
    [Fact]
    public async Task ATypedValueThatIsNotANumber_IsNamed_AndNotSent()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();

        desk.SeguroText = "cuatro mil";
        await desk.PreviewInFlight;

        Assert.Single(kit.PreviewRequests);
        Assert.Equal("El seguro no es un importe válido.", desk.InputProblem);
        Assert.False(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>D23: a result from an older input is dropped; the latest wins.</summary>
    [Fact]
    public async Task AResultFromAnOlderInput_IsDropped()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();
        var slow = new TaskCompletionSource<CollectionPreview>();
        kit.Desk.OnPreview = request => request.Seguro == 1m
            ? slow.Task
            : Task.FromResult(PreviewFor(request, Ledger, Today));

        desk.SeguroText = "1";
        var older = desk.PreviewInFlight;
        desk.SeguroText = "2";
        await desk.PreviewInFlight;

        slow.SetResult(PreviewFor(new CollectionRequest(Guid.Empty, null, null, 0m, Seguro: 1m), 700_000m, Today));
        await older;

        Assert.Equal("598.260", desk.LedgerFigureText);
        Assert.Equal("2", desk.LocatarioLines.Single(l => l.Label == "Seguro").AmountText);
        Assert.False(desk.Runner.IsBusy);
    }

    /// <summary>S3b review R3-adjustment-read-failure-hidden: a failed adjustment check says so, and does not block.</summary>
    [Fact]
    public async Task AFailedAdjustmentCheck_SaysSo_AndDoesNotBlock()
    {
        var kit = new CashDeskTestKit();
        var desk = kit.Build();
        kit.Adjustments.Gate = new TaskCompletionSource();
        kit.Adjustments.Gate.SetException(new InvalidOperationException("connection lost"));

        await desk.ChooseAccountAsync(Account());
        await TypeHandedAsync(desk, "598.260");

        Assert.Equal([DeskTexts.AdjustmentCheckFailed], desk.Warnings);
        Assert.True(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>S3b review R3-superseded-preview-error-shown: a superseded preview that fails shows no error.</summary>
    [Fact]
    public async Task ASupersededPreviewThatFails_ShowsNoError()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();
        var slow = new TaskCompletionSource<CollectionPreview>();
        kit.Desk.OnPreview = request => request.Seguro == 1m ? slow.Task : Task.FromResult(PreviewFor(request, Ledger, Today));

        desk.SeguroText = "1";
        var older = desk.PreviewInFlight;
        desk.SeguroText = "2";
        await desk.PreviewInFlight;
        slow.SetException(new InvalidOperationException("connection lost"));
        await older;

        Assert.Null(desk.Runner.ErrorMessage);
        Assert.Equal("2", desk.LocatarioLines.Single(l => l.Label == "Seguro").AmountText);
    }

    /// <summary>
    /// Live smoke H.5 (2026-10-24): typing the handed amount digit by digit broke the desk against Supabase,
    /// and the failure of each superseded preview was thrown away. It stays off the screen but is recorded.
    /// </summary>
    [Fact]
    public async Task ASupersededPreviewThatFails_IsRecordedInTheErrorLog()
    {
        var log = new RecordingErrorLog();
        var previous = OperationRunner.DefaultErrorLog;
        OperationRunner.DefaultErrorLog = log;
        try
        {
            var kit = new CashDeskTestKit();
            var desk = await kit.ChosenAsync();
            var slow = new TaskCompletionSource<CollectionPreview>();
            kit.Desk.OnPreview = request => request.Seguro == 1m ? slow.Task : Task.FromResult(PreviewFor(request, Ledger, Today));
            var failure = new InvalidOperationException("superseded preview failure " + Guid.NewGuid());

            desk.SeguroText = "1";
            var older = desk.PreviewInFlight;
            desk.SeguroText = "2";
            await desk.PreviewInFlight;
            slow.SetException(failure);
            await older;

            // Other test classes run in parallel with the default log installed; only this failure is ours.
            var entry = Assert.Single(log.Entries, e => ReferenceEquals(e.Exception, failure));
            Assert.Equal("Superseded read", entry.Context);
            Assert.Null(desk.Runner.ErrorMessage);
        }
        finally
        {
            OperationRunner.DefaultErrorLog = previous;
        }
    }

    /// <summary>
    /// Task 5.0a (d), S3b review R4-adjustment-check-uncancellable-blocks-picker: a slow adjustment check does
    /// not hold the picker; choosing another account cancels it, and nothing of the first account lands.
    /// </summary>
    [Fact]
    public async Task ASlowAdjustmentCheck_DoesNotHoldThePicker()
    {
        var kit = new CashDeskTestKit();
        var desk = kit.Build();
        var first = new AccountRow(Account());
        var second = new AccountRow(Account());
        kit.Adjustments.Gate = new TaskCompletionSource();

        var choosingFirst = desk.Picker.ChooseCommand.ExecuteAsync(first);
        Assert.True(desk.Picker.ChooseCommand.CanExecute(second));
        kit.Adjustments.Gate = null;
        await desk.Picker.ChooseCommand.ExecuteAsync(second);
        await choosingFirst;

        Assert.Same(second.Listing, desk.Account);
        Assert.Empty(desk.Warnings);
        Assert.All(kit.PreviewRequests, r => Assert.Equal(second.Listing.AccountId, r.AccountId));
        Assert.Null(desk.Runner.ErrorMessage);
    }
}
