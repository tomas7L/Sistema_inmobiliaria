using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
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
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static ContractDetail Detail(ContractStatus status) => new(
        ContractId, status, new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), false, null, null, null, null, null, 450_000m, 10, 8m, null, [], [], null, []);

    private static ContractDetailViewModel New(FakeContractRegistry port) =>
        new(port, new FakeAdjustmentDesk(), ContractId, new FakeClock(Today));

    /// <summary>A contract in force with its tenant, lessor and unit, as the canvas's "Registrar aviso" shows it.</summary>
    private static ContractDetail Peopled(ContractStatus status) => Detail(status) with
    {
        Parties =
        [
            new ContractPartyLine(Guid.NewGuid(), "Ruiz, Elena", PartyRole.Lessor),
            new ContractPartyLine(Guid.NewGuid(), "Ocampo, Martín", PartyRole.Tenant),
        ],
        Units = [new ContractUnitLine(Guid.NewGuid(), "Pellegrini 722", 100m)],
    };

    private static async Task<ContractDetailViewModel> LoadedAsync(ContractDetail detail)
    {
        var viewModel = New(new FakeContractRegistry { Detail = detail });
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static string Offered(ContractDetailViewModel v) => string.Join(
        ",",
        new[]
        {
            (v.IsEditOffered, "Editar"), (v.IsNoticeOffered, "Registrar aviso"), (v.IsUndoNoticeOffered, "Deshacer aviso"),
            (v.IsEndOffered, "Terminar"), (v.IsUndoEndOffered, "Deshacer fin"), (v.IsCancelOffered, "Cancelar por error"),
            (v.IsAccountOffered, "Ver cuenta"),
        }.Where(b => b.Item1).Select(b => b.Item2));

    /// <summary>Task 12.3d: the buttons each state offers, and a cancelled contract only its account.</summary>
    [Theory]
    [InlineData(ContractStatus.Active, null, "Editar,Registrar aviso,Terminar,Cancelar por error,Ver cuenta")]
    [InlineData(ContractStatus.PendingTermination, null, "Editar,Deshacer aviso,Terminar,Cancelar por error,Ver cuenta")]
    [InlineData(ContractStatus.Ended, EndReason.Expiry, "Editar,Deshacer fin,Cancelar por error,Ver cuenta")]
    [InlineData(ContractStatus.Ended, EndReason.LoadedByMistake, "Ver cuenta")]
    public async Task EachState_OffersItsButtons(ContractStatus status, EndReason? reason, string expected)
    {
        var viewModel = await LoadedAsync(Detail(status) with { EndReason = reason });

        Assert.Equal(expected, Offered(viewModel));
    }

    [Fact]
    public async Task BeforeTheContractIsRead_NothingIsOffered()
    {
        var viewModel = New(new FakeContractRegistry());

        Assert.Equal(string.Empty, Offered(viewModel));
        Assert.Null(viewModel.StatusText);
    }

    /// <summary>Task 12.3d: an ended contract corrects only its rate and its codeudores, and the screen says so.</summary>
    [Theory]
    [InlineData(ContractStatus.Active, false)]
    [InlineData(ContractStatus.PendingTermination, false)]
    [InlineData(ContractStatus.Ended, true)]
    public async Task OnlyAnEndedContract_EditsLimited(ContractStatus status, bool limited)
    {
        var viewModel = await LoadedAsync(Detail(status) with { EndReason = status == ContractStatus.Ended ? EndReason.Expiry : null });

        Assert.Equal(limited, viewModel.IsEditLimited);
    }

    [Theory]
    [InlineData(ContractStatus.Active, null, "Vigente")]
    [InlineData(ContractStatus.PendingTermination, null, "En rescisión")]
    [InlineData(ContractStatus.Ended, EndReason.MutualAgreement, "Finalizado")]
    [InlineData(ContractStatus.Ended, EndReason.LoadedByMistake, "Cancelado por error de carga")]
    public async Task TheState_IsNamed(ContractStatus status, EndReason? reason, string expected)
    {
        var viewModel = await LoadedAsync(Detail(status) with { EndReason = reason });

        Assert.Equal(expected, viewModel.StatusText);
    }

    /// <summary>Task 12.3d: the four reasons offered for ending, in the operator's words, and the one for an ended contract.</summary>
    [Fact]
    public async Task EndReasons_HaveTheirSpanishNames()
    {
        var viewModel = New(new FakeContractRegistry());

        Assert.Equal(
            ["Vencimiento del contrato", "Rescisión anticipada del inquilino", "Rescisión por incumplimiento", "Acuerdo de partes"],
            viewModel.EndReasons.Select(r => r.Text));

        var ended = await LoadedAsync(Detail(ContractStatus.Ended) with { EndReason = EndReason.TerminationForCause });
        Assert.Equal("Rescisión por incumplimiento", ended.EndReasonText);
    }

    /// <summary>The event log reads the reason, who and when, oldest first, as the port gave it.</summary>
    [Fact]
    public async Task TheEventLog_ReadsWhatWhyWhoAndWhen()
    {
        var at = new DateTimeOffset(2026, 11, 3, 14, 30, 0, TimeSpan.FromHours(-3));
        var viewModel = await LoadedAsync(Detail(ContractStatus.Active) with
        {
            Events = [new ContractEventLine(ContractEventKind.NoticeUndone, "Se cargó en otro contrato", null, "ana.torres", at)],
        });

        var row = Assert.Single(viewModel.Events);
        Assert.Equal("03/11/2026", row.WhenText);
        Assert.Equal("Aviso deshecho", row.EventText);
        Assert.Equal("Se cargó en otro contrato", row.ReasonText);
        Assert.Equal("ana.torres", row.WhoText);
        Assert.False(viewModel.HasNoEvents);
    }

    [Fact]
    public async Task AContractWithoutEvents_SaysSo()
    {
        var viewModel = await LoadedAsync(Detail(ContractStatus.Active));

        Assert.Empty(viewModel.Events);
        Assert.True(viewModel.HasNoEvents);
    }

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

    /// <summary>Canvas "Registrar aviso": a missing date is said under its own field, not over the screen.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ReviewingNotice_WithADateMissing_SaysSoUnderThatField(bool hasNotice, bool hasMoveOut)
    {
        var viewModel = New(new FakeContractRegistry());
        viewModel.NoticeDate = hasNotice ? Notice : null;
        viewModel.MoveOutDate = hasMoveOut ? MoveOut : null;

        viewModel.ReviewNoticeCommand.Execute(null);

        Assert.Equal(hasNotice ? null : ContractDetailViewModel.MissingNoticeDates, viewModel.NoticeDateError);
        Assert.Equal(hasMoveOut ? null : ContractDetailViewModel.MissingNoticeDates, viewModel.MoveOutDateError);
        Assert.Null(viewModel.RefusalMessage);
        Assert.False(viewModel.IsSummaryOpen);
    }

    /// <summary>
    /// Canvas "Registrar aviso" and task 12.3e: on Continuar, a notice after today and a move-out before the notice are each
    /// said under their field, both at once, before anything is written (spec Requirement 10's rules, as the port applies them).
    /// </summary>
    [Fact]
    public async Task ReviewingNotice_SaysEveryDateErrorUnderItsField_AtOnce()
    {
        var port = new FakeContractRegistry { Detail = Peopled(ContractStatus.Active) };
        var viewModel = New(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.NoticeDateText = "20/11/2026";
        viewModel.MoveOutDateText = "01/11/2026";

        viewModel.ReviewNoticeCommand.Execute(null);

        Assert.Equal(ContractRefusal.NoticeAfterToday.OperatorText(), viewModel.NoticeDateError);
        Assert.Equal(ContractRefusal.MoveOutBeforeNotice.OperatorText(), viewModel.MoveOutDateError);
        Assert.False(viewModel.IsSummaryOpen);
        Assert.Empty(port.Calls);
    }

    /// <summary>The dates are typed as the contract form's are: short, or run together.</summary>
    [Fact]
    public void TheNoticeDates_AreReadAsTyped()
    {
        var viewModel = New(new FakeContractRegistry());

        viewModel.NoticeDateText = "10/10/26";
        viewModel.MoveOutDateText = "31122026";

        Assert.Equal(Notice, viewModel.NoticeDate);
        Assert.Equal(MoveOut, viewModel.MoveOutDate);
    }

    /// <summary>
    /// Canvas "Registrar aviso": its own screen, in place of the detail, offered only where notice is (a contract in force),
    /// with the contract it is for, its term included so the dates can be chosen without leaving (Tomás, 2026-11-19: "que
    /// aparezcan los diferentes datos para poder rellenar esos campos"); "Volver al contrato" goes back to the detail.
    /// </summary>
    [Fact]
    public async Task TheNoticeScreen_OpensWhereNoticeIsOffered_ShowsTheContract_AndGoesBack()
    {
        var viewModel = await LoadedAsync(Peopled(ContractStatus.Active));

        Assert.True(viewModel.OpenNoticeCommand.CanExecute(null));
        viewModel.OpenNoticeCommand.Execute(null);

        Assert.Equal(ContractAction.Notice, viewModel.OpenAction);
        Assert.True(viewModel.IsActionOpen);
        Assert.Equal(
            [
                new DetailLine("Inquilino", "Ocampo, Martín"), new DetailLine("Propiedad", "Pellegrini 722"),
                new DetailLine("Vigencia", "01/01/2026 al 31/12/2027"), new DetailLine("Alquiler actual", "$ 450.000"),
            ],
            viewModel.ActionCardLines);

        viewModel.CloseActionCommand.Execute(null);

        Assert.Null(viewModel.OpenAction);
        Assert.False(viewModel.IsActionOpen);

        var withNotice = await LoadedAsync(Peopled(ContractStatus.PendingTermination));
        Assert.False(withNotice.OpenNoticeCommand.CanExecute(null));
    }

    /// <summary>Canvas "Registrar aviso": a notice registered ends on its notice, naming the tenant and the move-out.</summary>
    [Fact]
    public async Task ARegisteredNotice_EndsOnItsNotice_AndGoingBackForgetsIt()
    {
        var viewModel = await LoadedAsync(Peopled(ContractStatus.Active));
        viewModel.OpenNoticeCommand.Execute(null);
        viewModel.NoticeDateText = "10/10/2026";
        viewModel.MoveOutDateText = "31/12/2026";
        viewModel.ReviewNoticeCommand.Execute(null);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(
            new ActionDone("Aviso registrado", "El contrato de Ocampo, Martín quedó En rescisión. La desocupación está prevista para el 31/12/2026."),
            viewModel.Done);

        viewModel.CloseActionCommand.Execute(null);

        Assert.Null(viewModel.Done);
        Assert.Equal(string.Empty, viewModel.NoticeDateText);
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
        Assert.Null(viewModel.Done);
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

    /// <summary>Spec Decision 39, Requirement 10: an end before the month's due date names the month left uncharged.</summary>
    [Fact]
    public async Task TheEndSummary_NamesTheMonthLeftUncharged()
    {
        var port = new FakeContractRegistry { EndSummary = new ContractEndSummary(null, 0m, 0m, [], new DateOnly(2026, 11, 1)) };
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        var line = Assert.IsType<PendingEnd>(viewModel.PendingChange).UnchargedMonthLine;
        Assert.Contains("11/2026", line, StringComparison.Ordinal);
        Assert.Contains("no lo cobra", line, StringComparison.Ordinal);
        Assert.Contains("01/11/2026", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEndSummary_WithNoMonthLeftUncharged_SaysNothingAboutIt()
    {
        var port = new FakeContractRegistry { EndSummary = new ContractEndSummary(null, 0m, 0m, []) };
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        Assert.Null(Assert.IsType<PendingEnd>(viewModel.PendingChange).UnchargedMonthLine);
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

    /// <summary>A refusal the port finds on reviewing (the contract ended meanwhile) shows its text, and no summary opens.</summary>
    [Fact]
    public async Task ReviewingAnEnd_RefusedByThePort_ShowsItsText_AndOpensNoSummary()
    {
        var port = new FakeContractRegistry { EndSummary = ContractEndSummary.Refused(ContractRefusal.ContractEnded) };
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDate = EndOn;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.ContractEnded.OperatorText(), viewModel.RefusalMessage);
        Assert.False(viewModel.IsSummaryOpen);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
    }

    /// <summary>
    /// Spec scenario "ending with no reason is refused", on the screen (canvas "Terminar contrato"): a missing reason and a
    /// missing date are each said under their field, both at once, and the port is not asked.
    /// </summary>
    [Fact]
    public async Task ReviewingAnEnd_SaysEachMissingFieldUnderIt_WithoutAskingThePort()
    {
        var port = new FakeContractRegistry();
        var viewModel = New(port);

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.EndReasonMissing.OperatorText(), viewModel.EndReasonError);
        Assert.Equal(ContractDetailViewModel.MissingEndDate, viewModel.EndDateError);
        Assert.Null(viewModel.RefusalMessage);
        Assert.False(viewModel.IsSummaryOpen);
        Assert.Empty(port.Calls);

        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDateText = "05/11/2026";
        Assert.Null(viewModel.EndReasonError);
        Assert.Null(viewModel.EndDateError);
    }

    /// <summary>Canvas "Terminar contrato": an end after today or before the start is said under the date, before the port.</summary>
    [Theory]
    [InlineData("20/11/2026", ContractRefusal.EndAfterToday)]
    [InlineData("31/12/2025", ContractRefusal.EndBeforeContractStart)]
    public async Task ReviewingAnEnd_SaysTheDateRulesUnderTheDate(string typed, ContractRefusal expected)
    {
        var port = new FakeContractRegistry { Detail = Peopled(ContractStatus.Active) };
        var viewModel = New(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.EndReason = EndReason.Expiry;
        viewModel.EndDateText = typed;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        Assert.Equal(expected.OperatorText(), viewModel.EndDateError);
        Assert.False(viewModel.IsSummaryOpen);
        Assert.Empty(port.Calls);
    }

    /// <summary>
    /// Canvas "Terminar contrato": its own screen, offered where ending is, opening with nothing chosen; an end that lands ends
    /// on its own notice, saying it can be undone.
    /// </summary>
    [Fact]
    public async Task TheEndScreen_OpensEmpty_AndEndsOnItsNotice()
    {
        var port = new FakeContractRegistry { Detail = Peopled(ContractStatus.Active) };
        var viewModel = New(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.EndReason = EndReason.Expiry;

        Assert.True(viewModel.OpenEndCommand.CanExecute(null));
        viewModel.OpenEndCommand.Execute(null);

        Assert.Equal(ContractAction.End, viewModel.OpenAction);
        Assert.Null(viewModel.EndReason);
        Assert.Equal(string.Empty, viewModel.EndDateText);

        viewModel.EndReason = EndReason.MutualAgreement;
        viewModel.EndDateText = "05/11/2026";
        await viewModel.ReviewEndCommand.ExecuteAsync(null);
        Assert.Equal("Acuerdo de partes", Assert.IsType<PendingEnd>(viewModel.PendingChange).ReasonText);
        port.Detail = Peopled(ContractStatus.Ended);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(
            new ActionDone("Contrato terminado", "El contrato de Ocampo, Martín terminó el 05/11/2026. Si fue un error, se puede deshacer."),
            viewModel.Done);
        Assert.False(viewModel.OpenEndCommand.CanExecute(null));
    }

    /// <summary>Canvas "Terminar contrato": a missing date is said under the date, not over the screen.</summary>
    [Fact]
    public async Task ReviewingAnEnd_WithNoDate_SaysSo_WithoutAskingThePort()
    {
        var port = new FakeContractRegistry();
        var viewModel = New(port);
        viewModel.EndReason = EndReason.Expiry;

        await viewModel.ReviewEndCommand.ExecuteAsync(null);

        Assert.Equal(ContractDetailViewModel.MissingEndDate, viewModel.EndDateError);
        Assert.Null(viewModel.RefusalMessage);
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

        Assert.DoesNotContain(EndReason.LoadedByMistake, viewModel.EndReasons.Select(r => r.Reason));
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
        Assert.EndsWith("Esto es definitivo. Una vez cancelado, el contrato no se puede recuperar.", pending.Summary, StringComparison.Ordinal);
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

    /// <summary>
    /// Spec Requirement 24: undoing asks for a reason and is preceded by a summary. Canvas "Deshacer aviso": a missing reason
    /// is said under its field, and typing one clears it.
    /// </summary>
    [Fact]
    public async Task UndoingANotice_NeedsAReason_ThenConfirms_AndReadsAgain()
    {
        var port = new FakeContractRegistry { Detail = Detail(ContractStatus.PendingTermination) };
        var viewModel = New(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ChangeReason = "  ";

        viewModel.ReviewUndoNoticeCommand.Execute(null);

        Assert.Equal(ContractRefusal.ReasonMissing.OperatorText(), viewModel.ChangeReasonError);
        Assert.Null(viewModel.RefusalMessage);
        Assert.False(viewModel.IsSummaryOpen);

        viewModel.ChangeReason = "Otro";
        Assert.Null(viewModel.ChangeReasonError);

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

    /// <summary>
    /// Canvas "Deshacer aviso": its own screen, offered only where there is a notice, heading the contract with the notice's
    /// two dates; it opens with no reason typed, and an undone notice ends on its own notice.
    /// </summary>
    [Fact]
    public async Task TheUndoNoticeScreen_ShowsTheNoticeDates_AndEndsOnItsNotice()
    {
        var withNotice = Peopled(ContractStatus.PendingTermination) with { NoticeGivenDate = Notice, PlannedMoveOutDate = MoveOut };
        var port = new FakeContractRegistry { Detail = withNotice };
        var viewModel = New(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ChangeReason = "left from before";

        Assert.True(viewModel.OpenUndoNoticeCommand.CanExecute(null));
        viewModel.OpenUndoNoticeCommand.Execute(null);

        Assert.Equal(ContractAction.UndoNotice, viewModel.OpenAction);
        Assert.Equal(string.Empty, viewModel.ChangeReason);
        Assert.Equal(
            [new DetailLine("Fecha del aviso", "10/10/2026"), new DetailLine("Fecha de desocupación", "31/12/2026")],
            viewModel.ActionCardLines.TakeLast(2));

        viewModel.ChangeReason = "El inquilino decidió quedarse";
        viewModel.ReviewUndoNoticeCommand.Execute(null);
        port.Detail = Peopled(ContractStatus.Active);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(new ActionDone("Listo", "El aviso se deshizo. El contrato de Ocampo, Martín vuelve a estar Vigente."), viewModel.Done);

        var inForce = await LoadedAsync(Peopled(ContractStatus.Active));
        Assert.False(inForce.OpenUndoNoticeCommand.CanExecute(null));
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

    /// <summary>
    /// Canvas "Deshacer fin": its own screen, offered only on an ended contract, heading the contract with the end's date and
    /// reason; it opens with no reason typed, and an undone end ends on its own notice.
    /// </summary>
    [Fact]
    public async Task TheUndoEndScreen_ShowsTheEndDateAndReason_AndEndsOnItsNotice()
    {
        var ended = Peopled(ContractStatus.Ended) with { ActualEndDate = new DateOnly(2026, 11, 19), EndReason = EndReason.MutualAgreement };
        var port = new FakeContractRegistry { Detail = ended };
        var viewModel = New(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.ChangeReason = "left from before";

        Assert.True(viewModel.OpenUndoEndCommand.CanExecute(null));
        viewModel.OpenUndoEndCommand.Execute(null);

        Assert.Equal(ContractAction.UndoEnd, viewModel.OpenAction);
        Assert.Equal(string.Empty, viewModel.ChangeReason);
        Assert.Equal(
            [new DetailLine("Fecha de fin", "19/11/2026"), new DetailLine("Motivo del fin", "Acuerdo de partes")],
            viewModel.ActionCardLines.TakeLast(2));

        viewModel.ChangeReason = "El inquilino decidió quedarse";
        await viewModel.ReviewUndoEndCommand.ExecuteAsync(null);
        port.Detail = Peopled(ContractStatus.Active);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(new ActionDone("Listo", "El fin se deshizo. El contrato de Ocampo, Martín vuelve a estar Vigente."), viewModel.Done);

        var inForce = await LoadedAsync(Peopled(ContractStatus.Active));
        Assert.False(inForce.OpenUndoEndCommand.CanExecute(null));
    }

    /// <summary>Canvas "Deshacer fin": a missing reason is said under its field before the port is asked, and typing one clears it.</summary>
    [Fact]
    public async Task UndoingAnEnd_WithNoReason_SaysItUnderTheField_AndAsksNothing()
    {
        var port = new FakeContractRegistry { Detail = Detail(ContractStatus.Ended) };
        var viewModel = New(port);
        await viewModel.LoadCommand.ExecuteAsync(null);
        port.Calls.Clear();
        viewModel.ChangeReason = "  ";

        await viewModel.ReviewUndoEndCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.ReasonMissing.OperatorText(), viewModel.ChangeReasonError);
        Assert.Null(viewModel.RefusalMessage);
        Assert.False(viewModel.IsSummaryOpen);
        Assert.Empty(port.Calls);

        viewModel.ChangeReason = "Otro";
        Assert.Null(viewModel.ChangeReasonError);
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
