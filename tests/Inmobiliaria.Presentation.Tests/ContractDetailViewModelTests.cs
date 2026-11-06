using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirement 10 on the screen's logic, with the port faked: a summary precedes each, and ending shows what is owed.</summary>
public class ContractDetailViewModelTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly Notice = new(2026, 10, 10);
    private static readonly DateOnly MoveOut = new(2026, 12, 31);
    private static readonly DateOnly EndOn = new(2026, 11, 5);

    private static ContractDetail Detail(ContractStatus status) => new(
        ContractId, status, new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), false, null, null, null, null, null, 450_000m, 10, 8m, null, [], [], null, []);

    private static ContractDetailViewModel New(FakeContractRegistry port) => new(port, ContractId);

    [Fact]
    public async Task Loading_ReadsTheContract()
    {
        var port = new FakeContractRegistry { Detail = Detail(ContractStatus.Active) };
        var viewModel = New(port);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(port.Detail, viewModel.Detail);
    }

    /// <summary>Requirement 10: a summary precedes notice, and nothing is written until it is confirmed.</summary>
    [Fact]
    public void ReviewingNotice_OpensASummary_AndWritesNothing()
    {
        var port = new FakeContractRegistry();
        var viewModel = New(port);
        viewModel.NoticeDate = Notice;
        viewModel.MoveOutDate = MoveOut;

        viewModel.ReviewNoticeCommand.Execute(null);

        var pending = Assert.IsType<PendingNotice>(viewModel.PendingChange);
        Assert.Contains("10/10/2026", pending.Summary, StringComparison.Ordinal);
        Assert.Contains("31/12/2026", pending.Summary, StringComparison.Ordinal);
        Assert.True(viewModel.IsSummaryOpen);
        Assert.True(viewModel.ConfirmCommand.CanExecute(null));
        Assert.Empty(port.Calls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ReviewingNotice_WithADateMissing_SaysSo(bool hasNotice, bool hasMoveOut)
    {
        var viewModel = New(new FakeContractRegistry());
        viewModel.NoticeDate = hasNotice ? Notice : null;
        viewModel.MoveOutDate = hasMoveOut ? MoveOut : null;

        viewModel.ReviewNoticeCommand.Execute(null);

        Assert.Equal(ContractDetailViewModel.MissingNoticeDates, viewModel.RefusalMessage);
        Assert.False(viewModel.IsSummaryOpen);
    }

    [Fact]
    public async Task ConfirmingNotice_SendsTheDates_ClosesTheSummary_AndReadsAgain()
    {
        var port = new FakeContractRegistry { Detail = Detail(ContractStatus.PendingTermination) };
        var viewModel = New(port);
        viewModel.NoticeDate = Notice;
        viewModel.MoveOutDate = MoveOut;
        viewModel.ReviewNoticeCommand.Execute(null);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal<object?>(["GiveNotice", ContractId, Notice, MoveOut], Assert.Single(port.Calls));
        Assert.False(viewModel.IsSummaryOpen);
        Assert.Equal(ContractStatus.PendingTermination, viewModel.Detail!.Status);
    }

    /// <summary>Spec Test 27 on the screen: a refusal shows its own text, and the screen keeps what was typed.</summary>
    [Fact]
    public async Task ARefusedNotice_ShowsItsOwnText_AndKeepsTheDates()
    {
        var port = new FakeContractRegistry { OnChange = () => ContractResult.Refused(ContractRefusal.MoveOutBeforeNotice) };
        var viewModel = New(port);
        viewModel.NoticeDate = Notice;
        viewModel.MoveOutDate = MoveOut;
        viewModel.ReviewNoticeCommand.Execute(null);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.MoveOutBeforeNotice.OperatorText(), viewModel.RefusalMessage);
        Assert.Equal(Notice, viewModel.NoticeDate);
        Assert.False(viewModel.IsSummaryOpen);
    }

    /// <summary>Spec scenario: a contract owing 500,000 shows it, and debt does not stop the end.</summary>
    [Fact]
    public async Task ReviewingAnEnd_ShowsWhatIsOwed_AndWritesNothing()
    {
        var port = new FakeContractRegistry { EndSummary = new ContractEndSummary(null, 450_000m, 500_000m, []) };
        var viewModel = New(port);
        viewModel.EndReason = EndReason.MutualAgreement;
        viewModel.EndDate = EndOn;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        var pending = Assert.IsType<PendingEnd>(viewModel.PendingChange);
        Assert.Contains("500.000", pending.OwedLine, StringComparison.Ordinal);
        Assert.Null(pending.AlreadyWrittenLine);
        Assert.Equal<object?>(["SummarizeEnd", ContractId, EndReason.MutualAgreement, EndOn], Assert.Single(port.Calls));
        Assert.True(viewModel.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmingAnEnd_SendsTheReasonAndDate_AndReadsAgain()
    {
        var port = new FakeContractRegistry { Detail = Detail(ContractStatus.Ended) };
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;
        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal<object?>(["End", ContractId, EndReason.Expiry, EndOn], port.Calls[^1]);
        Assert.False(viewModel.IsSummaryOpen);
        Assert.Equal(ContractStatus.Ended, viewModel.Detail!.Status);
    }

    /// <summary>Spec Requirement 10: a period already written stays, and the summary says so.</summary>
    [Fact]
    public async Task TheEndSummary_NamesPeriodsAlreadyWrittenBeyondTheDate()
    {
        var port = new FakeContractRegistry
        {
            EndSummary = new ContractEndSummary(null, 900_000m, 950_000m, [new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)]),
        };
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        var line = Assert.IsType<PendingEnd>(viewModel.PendingChange).AlreadyWrittenLine;
        Assert.Contains("11/2026", line, StringComparison.Ordinal);
        Assert.Contains("12/2026", line, StringComparison.Ordinal);
    }

    /// <summary>With invalid recargo settings the screen says the figure leaves recargo out, instead of passing the ledger off as what is owed.</summary>
    [Fact]
    public async Task TheEndSummary_WithoutRecargoTerms_SaysTheFigureLeavesItOut()
    {
        var port = new FakeContractRegistry { EndSummary = new ContractEndSummary(null, 450_000m, null, []) };
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        var line = Assert.IsType<PendingEnd>(viewModel.PendingChange).OwedLine;
        Assert.Contains("450.000", line, StringComparison.Ordinal);
        Assert.Contains("sin contar recargo", line, StringComparison.Ordinal);
    }

    /// <summary>"Queda adeudado $0, que se puede seguir cobrando" says nothing an operator can use.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheEndSummary_WithNothingOwed_SaysSo(bool withRecargoTerms)
    {
        var port = new FakeContractRegistry { EndSummary = new ContractEndSummary(null, 0m, withRecargoTerms ? 0m : null, []) };
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        Assert.Equal(PendingEnd.NothingOwed, Assert.IsType<PendingEnd>(viewModel.PendingChange).OwedLine);
    }

    /// <summary>Spec scenario: ending with no reason is refused, and no summary opens.</summary>
    [Fact]
    public async Task ReviewingAnEnd_RefusedByThePort_ShowsItsText_AndOpensNoSummary()
    {
        var port = new FakeContractRegistry { EndSummary = ContractEndSummary.Refused(ContractRefusal.EndReasonMissing) };
        var viewModel = New(port);
        viewModel.EndDate = EndOn;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.EndReasonMissing.OperatorText(), viewModel.RefusalMessage);
        Assert.False(viewModel.IsSummaryOpen);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReviewingAnEnd_WithNoDate_SaysSo_WithoutAskingThePort()
    {
        var port = new FakeContractRegistry();
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        Assert.Equal(ContractDetailViewModel.MissingEndDate, viewModel.RefusalMessage);
        Assert.Empty(port.Calls);
    }

    /// <summary>What is confirmed is what was read: editing anything it was built from closes the summary.</summary>
    [Theory]
    [InlineData("notice")]
    [InlineData("moveOut")]
    [InlineData("reason")]
    [InlineData("endDate")]
    public async Task EditingAfterASummary_ClosesIt(string edit)
    {
        var port = new FakeContractRegistry();
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;
        await viewModel.ReviewEndCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsSummaryOpen);

        switch (edit)
        {
            case "notice": viewModel.NoticeDate = Notice; break;
            case "moveOut": viewModel.MoveOutDate = MoveOut; break;
            case "reason": viewModel.EndReason = EndReason.TerminationForCause; break;
            case "endDate": viewModel.EndDate = EndOn.AddDays(-1); break;
            default: throw new ArgumentOutOfRangeException(nameof(edit), edit, "No such edit.");
        }

        Assert.False(viewModel.IsSummaryOpen);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_ClosesTheSummary_AndWritesNothing()
    {
        var port = new FakeContractRegistry();
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;
        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        viewModel.CancelCommand.Execute(null);

        Assert.False(viewModel.IsSummaryOpen);
        Assert.Equal("SummarizeEnd", Assert.Single(port.Calls)[0]);
    }

    /// <summary>
    /// A write already out cannot be called back, so "cancel" must not look as if it did: an operator could believe an end was
    /// abandoned when it was saved. From the gentle-ai review of S4 (R4-cancel-during-commit).
    /// </summary>
    [Fact]
    public async Task CancelIsNotOffered_WhileAWriteIsOut()
    {
        var gate = new TaskCompletionSource();
        var port = new FakeContractRegistry { ChangeGate = gate };
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;
        await viewModel.ReviewEndCommand.ExecuteAsync(null);
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        var told = 0;
        viewModel.CancelCommand.CanExecuteChanged += (_, _) => told++;

        var confirming = viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.False(viewModel.CancelCommand.CanExecute(null));

        // A bound button only asks again when it is told to.
        Assert.True(told > 0, "The button was not told the write started.");
        var toldWhenItStarted = told;
        gate.SetResult();
        await confirming;
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        Assert.True(told > toldWhenItStarted, "The button was not told the write finished.");
    }

    /// <summary>Spec Test 89: "Cargado por error" belongs to cancelling, and an ordinary end never offers it.</summary>
    [Fact]
    public void LoadedByMistake_IsNotOfferedWhenEnding()
    {
        var viewModel = New(new FakeContractRegistry());

        Assert.DoesNotContain(EndReason.LoadedByMistake, viewModel.EndReasons);
        Assert.Equal(Enum.GetValues<EndReason>().Length - 1, viewModel.EndReasons.Count);
    }

    /// <summary>Spec Requirement 23: the summary names the tenant, the units and the exact amount, and nothing is written until it is confirmed.</summary>
    [Fact]
    public async Task ReviewingACancellation_NamesWhoWhatAndHowMuch_AndWritesNothing()
    {
        var port = new FakeContractRegistry();
        var viewModel = New(port);
        viewModel.ChangeReason = "Inquilino equivocado";

        await viewModel.ReviewCancelCommand.ExecuteAsync(null);

        var pending = Assert.IsType<PendingCancel>(viewModel.PendingChange);
        Assert.Contains("Pérez, Juan", pending.Summary, StringComparison.Ordinal);
        Assert.Contains("Alem 1258", pending.Summary, StringComparison.Ordinal);
        Assert.Contains("900.000", pending.Summary, StringComparison.Ordinal);
        Assert.Null(pending.RetainedLine);
        Assert.Equal("SummarizeCancel", Assert.Single(port.Calls)[0]);
    }

    /// <summary>The port compares what the operator read with what it would write, so the summary itself has to go back.</summary>
    [Fact]
    public async Task ConfirmingACancellation_SendsTheReasonAndTheSummaryRead_AndReadsAgain()
    {
        var port = new FakeContractRegistry { Detail = Detail(ContractStatus.Ended) };
        var viewModel = New(port);
        viewModel.ChangeReason = "Inquilino equivocado";
        await viewModel.ReviewCancelCommand.ExecuteAsync(null);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal<object?>(["Cancel", ContractId, "Inquilino equivocado", port.CancelSummary], port.Calls[^1]);
        Assert.False(viewModel.IsSummaryOpen);
        Assert.Equal(ContractStatus.Ended, viewModel.Detail!.Status);
    }

    /// <summary>Spec Test 76 and Test 90 on the screen: the refusal names the recibos, and says when only an Admin can void them.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ABlockedCancellation_NamesTheRecibos(bool admin)
    {
        var port = new FakeContractRegistry
        {
            CancelSummary = new ContractCancellationSummary(ContractRefusal.PaymentMustBeVoidedFirst, string.Empty, string.Empty, 0m, [31001, 31002], admin, 0m, [], string.Empty),
        };
        var viewModel = New(port);
        viewModel.ChangeReason = "Error";

        await viewModel.ReviewCancelCommand.ExecuteAsync(null);

        Assert.Contains("31001, 31002", viewModel.RefusalMessage, StringComparison.Ordinal);
        Assert.Equal(admin, viewModel.RefusalMessage!.Contains("Admin", StringComparison.Ordinal));
        Assert.False(viewModel.IsSummaryOpen);
    }

    /// <summary>The refusal raised on confirming names the recibos too: the port's own text wins over the generic one.</summary>
    [Fact]
    public async Task ABlockedConfirm_ShowsThePortsText()
    {
        var port = new FakeContractRegistry
        {
            OnChange = () => ContractResult.Refused(ContractRefusal.PaymentMustBeVoidedFirst) with { RefusalText = "Primero hay que anular los recibos 31001, 31002." },
        };
        var viewModel = New(port);
        viewModel.ChangeReason = "Error";
        await viewModel.ReviewCancelCommand.ExecuteAsync(null);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Contains("31001, 31002", viewModel.RefusalMessage, StringComparison.Ordinal);
    }

    /// <summary>Spec Decision 36: money past the voiding window stays with its recibos, and the summary says it is returned by hand.</summary>
    [Fact]
    public async Task TheCancelSummary_SaysRetainedMoneyIsReturnedByHand()
    {
        var port = new FakeContractRegistry
        {
            CancelSummary = new ContractCancellationSummary(null, "Pérez, Juan", "Alem 1258", 450_000m, [], false, 300_000m, [31001, 31002], "plan"),
        };
        var viewModel = New(port);
        viewModel.ChangeReason = "Error";

        await viewModel.ReviewCancelCommand.ExecuteAsync(null);

        var line = Assert.IsType<PendingCancel>(viewModel.PendingChange).RetainedLine;
        Assert.Contains("31001, 31002", line, StringComparison.Ordinal);
        Assert.Contains("300.000", line, StringComparison.Ordinal);
        Assert.Contains("a mano", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingTheReason_ClosesTheSummary()
    {
        var viewModel = New(new FakeContractRegistry());
        viewModel.ChangeReason = "Error";
        await viewModel.ReviewCancelCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsSummaryOpen);

        viewModel.ChangeReason = "Otro motivo";

        Assert.False(viewModel.IsSummaryOpen);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
    }

    /// <summary>Spec Requirement 24: undoing asks for a reason and is preceded by a summary.</summary>
    [Fact]
    public async Task UndoingANotice_NeedsAReason_ThenConfirms_AndReadsAgain()
    {
        var port = new FakeContractRegistry { Detail = Detail(ContractStatus.PendingTermination) };
        var viewModel = New(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ChangeReason = "  ";

        viewModel.ReviewUndoNoticeCommand.Execute(null);

        Assert.Equal(ContractRefusal.ReasonMissing.OperatorText(), viewModel.RefusalMessage);
        Assert.False(viewModel.IsSummaryOpen);

        viewModel.ChangeReason = "Otro contrato";
        viewModel.ReviewUndoNoticeCommand.Execute(null);
        Assert.IsType<PendingUndoNotice>(viewModel.PendingChange);
        Assert.Empty(port.Calls);
        port.Detail = Detail(ContractStatus.Active);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal<object?>(["UndoNotice", ContractId, "Otro contrato"], Assert.Single(port.Calls));
        Assert.False(viewModel.IsSummaryOpen);
        Assert.Equal(ContractStatus.Active, viewModel.Detail!.Status);
    }

    /// <summary>Spec Requirement 24: periods between the end and the undo accrue as usual, with the summary naming them.</summary>
    [Fact]
    public async Task UndoingAnEnd_NamesThePeriodsThatWillAccrue_ThenConfirms()
    {
        var port = new FakeContractRegistry
        {
            UndoEndSummary = new ContractUndoEndSummary(null, null, [new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)]),
        };
        var viewModel = New(port);
        viewModel.ChangeReason = "Terminado por error";

        await viewModel.ReviewUndoEndCommand.ExecuteAsync(null);

        var pending = Assert.IsType<PendingUndoEnd>(viewModel.PendingChange);
        Assert.Contains("10/2026, 11/2026", pending.Summary, StringComparison.Ordinal);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal<object?>(["UndoEnd", ContractId, "Terminado por error"], port.Calls[^1]);
    }

    [Fact]
    public async Task AnUndoBlockedByAUnitLetAgain_NamesTheTenant_AndOpensNoSummary()
    {
        var port = new FakeContractRegistry
        {
            UndoEndSummary = new ContractUndoEndSummary(ContractRefusal.UnitCoveredByAnotherContract, "Gómez, Ana", []),
        };
        var viewModel = New(port);
        viewModel.ChangeReason = "Error";

        await viewModel.ReviewUndoEndCommand.ExecuteAsync(null);

        Assert.Contains("Gómez, Ana", viewModel.RefusalMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsSummaryOpen);
    }

    /// <summary>Spec Requirement 23: it stays readable, marked as cancelled, and an ordinary end reads as ended.</summary>
    [Fact]
    public void TheList_MarksACancelledContract()
    {
        var ended = FakeContractRegistry.Saved with { Status = ContractStatus.Ended };

        Assert.Equal("Cancelado por error de carga", new ContractRow(ended with { IsCancelled = true }).StatusText);
        Assert.NotEqual("Cancelado por error de carga", new ContractRow(ended).StatusText);
    }

    /// <summary>Test 71: every refusal reaches the operator in words.</summary>
    [Fact]
    public void EveryRefusal_HasOperatorText()
    {
        foreach (var refusal in Enum.GetValues<ContractRefusal>())
        {
            Assert.False(string.IsNullOrWhiteSpace(refusal.OperatorText()));
        }
    }

    [Fact]
    public void TheCorrectionRefusals_SayItIsCancelledAndCreatedAgain()
    {
        Assert.Contains("cancelar el contrato y cargarlo de nuevo", ContractRefusal.AccountHasMovements.OperatorText(), StringComparison.Ordinal);
        Assert.Contains("cancelar el contrato y cargarlo de nuevo", ContractRefusal.AdjustmentConfirmed.OperatorText(), StringComparison.Ordinal);
        Assert.Contains("cancelar el contrato y cargarlo de nuevo", ContractRefusal.UnitCountChanged.OperatorText(), StringComparison.Ordinal);
    }
}
