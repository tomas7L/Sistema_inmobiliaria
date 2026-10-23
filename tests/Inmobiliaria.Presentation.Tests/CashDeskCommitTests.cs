using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.CashDesk;
using Inmobiliaria.Presentation.Common;
using static Inmobiliaria.Presentation.Tests.CashDeskTestKit;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec tests 5, 15 to 18, design sections 7.1, 7.2 and 7.4.</summary>
public class CashDeskCommitTests
{
    private static async Task<CashDeskViewModel> SummaryAsync(CashDeskTestKit kit)
    {
        var desk = await kit.ChosenAsync();
        await TypeHandedAsync(desk, "598.260");
        await desk.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DeskState.Summary, desk.State);
        return desk;
    }

    /// <summary>Spec test 15: the commit is unavailable until an amount is typed and a committable summary is shown.</summary>
    [Fact]
    public async Task TheCommit_NeedsATypedAmountAndASummary()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();
        Assert.False(desk.CommitCommand.CanExecute(null));

        await TypeHandedAsync(desk, "598.260");
        Assert.False(desk.CommitCommand.CanExecute(null));

        await desk.ContinueCommand.ExecuteAsync(null);
        Assert.True(desk.CommitCommand.CanExecute(null));
        Assert.Same(kit.PreviewRequests[^1], desk.Summary!.Request);
    }

    /// <summary>Spec test 15: changing any input withdraws the summary.</summary>
    [Theory]
    [InlineData("received")]
    [InlineData("period")]
    [InlineData("seguro")]
    [InlineData("handed")]
    public async Task ChangingAnyInput_WithdrawsTheSummary(string input)
    {
        var kit = new CashDeskTestKit();
        var desk = await SummaryAsync(kit);

        switch (input)
        {
            case "received": desk.ReceivedOnInput = new DateTime(2026, 1, 14); break;
            case "period": desk.NamedPeriod = new DateTime(2026, 1, 1); break;
            case "seguro": desk.SeguroText = "10"; break;
            case "handed": desk.HandedAmountText = "598.261"; break;
        }

        Assert.Null(desk.Summary);
        Assert.False(desk.CommitCommand.CanExecute(null));
        await desk.PreviewInFlight;
    }

    /// <summary>Spec test 16: the commit sends the exact pair shown, and a success shows both numbers and both recibos.</summary>
    [Fact]
    public async Task ASuccessfulCommit_ShowsBothNumbersLocatarioFirst_AndBothRecibos()
    {
        var kit = new CashDeskTestKit();
        var collectionId = Guid.NewGuid();
        kit.Reader.Payments[collectionId] = Issued(collectionId, 31001);
        (CollectionPreview, CollectionRequest)? sent = null;
        kit.Desk.OnCommit = (preview, request) =>
        {
            sent = (preview, request);
            return Task.FromResult(CollectionResult.Committed(collectionId, 31001));
        };
        var desk = await SummaryAsync(kit);
        var shown = desk.Summary!;

        await desk.CommitCommand.ExecuteAsync(null);

        Assert.Equal((shown.Preview, shown.Request), sent);
        Assert.Equal(DeskState.Issued, desk.State);
        Assert.Equal("Recibo locatario N° 31001 · Recibo propietario N° 31002", desk.IssuedText);
        Assert.Equal([collectionId], kit.Reader.CollectionReads);
        Assert.Equal(["N° 31001", "N° 31002"], desk.Recibo!.Copies.Select(c => c.NumberText));
        Assert.Null(desk.Summary);
    }

    /// <summary>Spec Requirement 9: a refused commit shows its text and no numbers.</summary>
    [Fact]
    public async Task ARefusedCommit_ShowsTheRefusal_AndNoNumbers()
    {
        var kit = new CashDeskTestKit();
        kit.Desk.OnCommit = (_, _) => Task.FromResult(CollectionResult.Refused(CollectionRefusal.PeriodAlreadySettled));
        var desk = await SummaryAsync(kit);

        await desk.CommitCommand.ExecuteAsync(null);

        Assert.Equal(DeskState.Refused, desk.State);
        Assert.Equal(CollectionRefusal.PeriodAlreadySettled.OperatorText(), desk.RefusalMessage);
        Assert.Null(desk.IssuedText);
        Assert.Null(desk.Recibo);
    }

    /// <summary>
    /// Spec test 17: a stale or concurrent refusal causes exactly one fresh preview, shows what differs,
    /// clears the confirmation and keeps the typed amount; the commit is never sent again.
    /// </summary>
    [Theory]
    [InlineData(CollectionRefusal.PreviewStale)]
    [InlineData(CollectionRefusal.ConcurrentChange)]
    public async Task AStaleCommit_PreviewsOnce_ShowsTheDifference_AndNeverRetries(CollectionRefusal refusal)
    {
        var kit = new CashDeskTestKit();
        kit.Desk.OnCommit = (_, _) =>
        {
            kit.LedgerFigure = 605_000m;
            return Task.FromResult(CollectionResult.Refused(refusal));
        };
        var desk = await SummaryAsync(kit);
        var previewsBefore = kit.PreviewRequests.Count;

        await desk.CommitCommand.ExecuteAsync(null);

        Assert.Equal(1, kit.Desk.CommitCalls);
        Assert.Equal(previewsBefore + 1, kit.PreviewRequests.Count);
        Assert.Equal(DeskState.Stale, desk.State);
        Assert.Equal(refusal.OperatorText(), desk.StaleMessage);
        Assert.Contains(desk.Differences, d => d.What == "Saldo del período" && d.Before == "598.260" && d.After == "605.000");
        Assert.Null(desk.Summary);
        Assert.Equal("598.260", desk.HandedAmountText);
        Assert.Equal(CollectionRefusal.AmountNotTotal.OperatorText(), desk.RefusalMessage);
        Assert.False(desk.ContinueCommand.CanExecute(null));
        Assert.False(desk.CommitCommand.CanExecute(null));
    }

    /// <summary>Spec test 18: a double click sends one commit.</summary>
    [Fact]
    public async Task ADoubleClick_SendsOneCommit()
    {
        var kit = new CashDeskTestKit();
        var gate = new TaskCompletionSource<CollectionResult>();
        kit.Desk.OnCommit = (_, _) => gate.Task;
        var desk = await SummaryAsync(kit);

        var first = desk.CommitCommand.ExecuteAsync(null);
        Assert.False(desk.CommitCommand.CanExecute(null));
        var second = desk.CommitCommand.ExecuteAsync(null);

        gate.SetResult(CollectionResult.Refused(CollectionRefusal.PeriodAlreadySettled));
        await Task.WhenAll(first, second);

        Assert.Equal(1, kit.Desk.CommitCalls);
    }

    /// <summary>
    /// Spec tests 5 and 18 (S1 review R3-typed-values-test-vacuous): a commit that raises shows the
    /// "may or may not have been recorded" text, keeps the account and every typed value on screen, and the
    /// screen is usable again.
    /// </summary>
    [Fact]
    public async Task AFailedCommit_KeepsTheTypedValues_AndTheCommandsReturn()
    {
        var kit = new CashDeskTestKit();
        kit.Desk.OnCommit = (_, _) => throw new InvalidOperationException("Host=db;Password=secret");
        var desk = await kit.ChosenAsync();
        desk.SeguroText = "4.000";
        await desk.PreviewInFlight;
        desk.ReceivedOnInput = new DateTime(2026, 1, 14);
        await desk.PreviewInFlight;
        kit.LedgerFigure = Ledger;
        await TypeHandedAsync(desk, "598.260");
        await desk.ContinueCommand.ExecuteAsync(null);
        var account = desk.Account;

        await desk.CommitCommand.ExecuteAsync(null);

        Assert.Equal(DeskState.Failed, desk.State);
        Assert.Equal(ErrorMessages.CommitFailed, desk.Runner.ErrorMessage);
        Assert.Same(account, desk.Account);
        Assert.Equal(("598.260", "4.000", new DateTime(2026, 1, 14)), (desk.HandedAmountText, desk.SeguroText, desk.ReceivedOnInput));
        Assert.False(desk.Runner.IsBusy);
        Assert.True(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>Choosing another account clears what the previous one left on screen.</summary>
    [Fact]
    public async Task ChoosingAnotherAccount_StartsAgain()
    {
        var kit = new CashDeskTestKit();
        var desk = await SummaryAsync(kit);
        var other = Account();

        await desk.ChooseAccountAsync(other);

        Assert.Same(other, desk.Account);
        Assert.Null(desk.Summary);
        Assert.Equal(string.Empty, desk.HandedAmountText);
        Assert.Equal(DeskState.Owed, desk.State);
        Assert.Equal(new CollectionRequest(other.AccountId, null, null, 0m), kit.PreviewRequests[^1]);
    }

    /// <summary>
    /// S3b review R3-failed-preview-keeps-stale-request: a preview that fails after a committable one leaves
    /// nothing to continue with, so the summary can never be built from the inputs the operator replaced.
    /// </summary>
    [Fact]
    public async Task AFailedPreview_AfterACommittableOne_LeavesNothingToContinueWith()
    {
        var kit = new CashDeskTestKit();
        var desk = await kit.ChosenAsync();
        await TypeHandedAsync(desk, "598.260");
        Assert.True(desk.ContinueCommand.CanExecute(null));
        kit.Desk.OnPreview = _ => throw new InvalidOperationException("connection lost");

        desk.SeguroText = "4.000";
        await desk.PreviewInFlight;

        Assert.Null(desk.Preview);
        Assert.Equal(ErrorMessages.Generic, desk.Runner.ErrorMessage);
        Assert.False(desk.ContinueCommand.CanExecute(null));
        await desk.ContinueCommand.ExecuteAsync(null);
        Assert.Null(desk.Summary);
    }

    /// <summary>
    /// S3b review, the worse variant: a preview of the previous account that lands while the new account's
    /// adjustments are being read is never applied, so the previous account's request cannot be committed
    /// under the new account's header when the new account's own preview fails.
    /// </summary>
    [Fact]
    public async Task APreviewOfThePreviousAccount_IsNeverAppliedToTheNewOne()
    {
        var kit = new CashDeskTestKit();
        var first = Account();
        var second = Account();
        var slowFirst = new TaskCompletionSource<CollectionPreview>();
        var desk = await kit.ChosenAsync(first);
        kit.Desk.OnPreview = request =>
            request.AccountId == first.AccountId ? slowFirst.Task : throw new InvalidOperationException("connection lost");

        desk.HandedAmountText = "598.260";
        var firstPreview = desk.PreviewInFlight;
        kit.Adjustments.Gate = new TaskCompletionSource();
        var choosing = desk.ChooseAccountAsync(second);

        slowFirst.SetResult(PreviewFor(new CollectionRequest(first.AccountId, null, null, Ledger), Ledger, Today));
        await firstPreview;
        kit.Adjustments.Gate.SetResult();
        await choosing;
        await TypeHandedAsync(desk, "598.260");

        Assert.Same(second, desk.Account);
        Assert.Null(desk.Preview);
        Assert.False(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>S3b review R3-account-switch-during-commit: no other account can be chosen while a commit runs.</summary>
    [Fact]
    public async Task AnotherAccount_CannotBeChosenWhileACommitRuns()
    {
        var kit = new CashDeskTestKit();
        var gate = new TaskCompletionSource<CollectionResult>();
        kit.Desk.OnCommit = (_, _) => gate.Task;
        var desk = await SummaryAsync(kit);
        var first = desk.Account;
        var row = new AccountRow(Account());

        var committing = desk.CommitCommand.ExecuteAsync(null);
        Assert.False(desk.Picker.ChooseCommand.CanExecute(row));
        await desk.ChooseAccountAsync(row.Listing);
        Assert.Same(first, desk.Account);

        gate.SetResult(CollectionResult.Refused(CollectionRefusal.PeriodAlreadySettled));
        await committing;
        Assert.True(desk.Picker.ChooseCommand.CanExecute(row));
    }

    /// <summary>
    /// S3b review R4-input-edit-during-commit-regresses-state: an input changed while the commit runs starts
    /// no preview then; a payment that was issued stays issued.
    /// </summary>
    [Fact]
    public async Task AnInputChangedDuringTheCommit_DoesNotUndoTheIssuedState()
    {
        var kit = new CashDeskTestKit();
        var collectionId = Guid.NewGuid();
        kit.Reader.Payments[collectionId] = Issued(collectionId, 31001);
        var gate = new TaskCompletionSource<CollectionResult>();
        kit.Desk.OnCommit = (_, _) => gate.Task;
        var desk = await SummaryAsync(kit);
        var previews = kit.PreviewRequests.Count;

        var committing = desk.CommitCommand.ExecuteAsync(null);
        desk.SeguroText = "100";
        gate.SetResult(CollectionResult.Committed(collectionId, 31001));
        await committing;
        await desk.PreviewInFlight;

        Assert.Equal(previews, kit.PreviewRequests.Count);
        Assert.Equal(DeskState.Issued, desk.State);
        Assert.False(desk.ContinueCommand.CanExecute(null));
    }

    /// <summary>
    /// S3b review R4-stale-repreview-bypasses-latest-wins: an input changed while the stale re-preview runs
    /// wins, and the confirmed request is never put back under it.
    /// </summary>
    [Fact]
    public async Task AnInputChangedDuringTheStaleRepreview_Wins()
    {
        var kit = new CashDeskTestKit();
        kit.Desk.OnCommit = (_, _) => Task.FromResult(CollectionResult.Refused(CollectionRefusal.ConcurrentChange));
        var desk = await SummaryAsync(kit);
        var slowFresh = new TaskCompletionSource<CollectionPreview>();
        var stale = true;
        kit.Desk.OnPreview = request =>
        {
            kit.PreviewRequests.Add(request);
            if (stale)
            {
                stale = false;
                return slowFresh.Task;
            }

            return Task.FromResult(PreviewFor(request, Ledger, Today));
        };

        var committing = desk.CommitCommand.ExecuteAsync(null);
        desk.SeguroText = "100";
        await desk.PreviewInFlight;
        slowFresh.SetResult(PreviewFor(kit.PreviewRequests[^1], 605_000m, Today));
        await committing;
        await desk.PreviewInFlight;

        await desk.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(100m, desk.Summary!.Request.Seguro);
        Assert.Equal("598.260", desk.LedgerFigureText);
    }

    /// <summary>S3b review: when the stale re-preview fails, the rejected pair cannot be confirmed again.</summary>
    [Fact]
    public async Task AFailedStaleRepreview_LeavesNothingToConfirmAgain()
    {
        var kit = new CashDeskTestKit();
        kit.Desk.OnCommit = (_, _) =>
        {
            kit.Desk.OnPreview = _ => throw new InvalidOperationException("connection lost");
            return Task.FromResult(CollectionResult.Refused(CollectionRefusal.PreviewStale));
        };
        var desk = await SummaryAsync(kit);

        await desk.CommitCommand.ExecuteAsync(null);

        Assert.Equal(DeskState.Stale, desk.State);
        Assert.Null(desk.Preview);
        Assert.False(desk.ContinueCommand.CanExecute(null));
    }
}
