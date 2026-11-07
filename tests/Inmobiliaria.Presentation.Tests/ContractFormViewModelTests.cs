using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>gestion-de-alquileres spec Requirements 6 to 8 and Tests 10, 17, 19 and 22 on the screens' logic, with the port faked.</summary>
public class ContractFormViewModelTests
{
    private static readonly PartyListing Juan = new(Guid.NewGuid(), "Juan", "Pérez", "30123456", null, null, null, null);
    private static readonly PartyListing Ana = new(Guid.NewGuid(), "Ana", "Gómez", "28111222", null, null, null, null);
    private static readonly PartyListing Raul = new(Guid.NewGuid(), "Raúl", "Vera", "27222333", null, null, null, null);

    private static UnitListing Unit(string number = "1258") =>
        new(Guid.NewGuid(), UnitType.Property, new Address("Alem", number, "Sunchales", "Santa Fe", "2322"), null);

    private static async Task<ContractFormViewModel> FilledAsync(FakeContractRegistry port, bool admin = false)
    {
        var viewModel = new ContractFormViewModel(port, admin) { Tenant = Juan };
        await viewModel.AddLessorCommand.ExecuteAsync(Ana);
        viewModel.Units.Add(new ContractFormUnit(Unit()));
        viewModel.StartDate = new DateOnly(2026, 12, 1);
        viewModel.NominalEndDate = new DateOnly(2028, 11, 30);
        viewModel.MonthlyRentText = "450.000";
        viewModel.HonorariosText = "8";
        return viewModel;
    }

    /// <summary>Text that is no number is refused, never replaced by the suggestion; a number out of range is the port's to refuse.</summary>
    [Fact]
    public async Task ATypedClauseOffset_IsNeverReplacedByTheSuggestion()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        var ipc = Guid.NewGuid();
        viewModel.ClauseIndexNames = new Dictionary<Guid, string> { [ipc] = "IPC" };
        viewModel.ClauseIndexIds.Add(ipc);
        viewModel.ClauseIntervalText = "6";
        viewModel.ClauseOffsetText = "x";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(port.Created);
        Assert.Equal(ContractRefusal.ClauseInvalid.OperatorText(), viewModel.RefusalMessage);

        viewModel.ClauseOffsetText = "4";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(4, Assert.Single(port.Created).Draft.Clause!.WindowOffsetMonths);
    }

    /// <summary>Spec Decision 15: an offset left blank takes the suggestion of the indices chosen, as the months shown said.</summary>
    [Fact]
    public async Task AClauseOffsetLeftBlank_SendsTheSuggestion()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        var ipc = Guid.NewGuid();
        viewModel.ClauseIndexNames = new Dictionary<Guid, string> { [ipc] = "IPC" };
        viewModel.ClauseIndexIds.Add(ipc);
        viewModel.ClauseIntervalText = "6";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(2, Assert.Single(port.Created).Draft.Clause!.WindowOffsetMonths);
    }

    /// <summary>Spec Requirement 7: what the form holds reaches the port, a lone unit takes 100% and the due day defaults to 10.</summary>
    [Fact]
    public async Task Saving_SendsTheFormToThePort_AndClearsIt()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.Codebtors.Add(Raul);
        Assert.Equal("10", viewModel.DueDayText);

        await viewModel.SaveCommand.ExecuteAsync(null);

        var (draft, accepted) = Assert.Single(port.Created);
        Assert.False(accepted);
        Assert.Equal(Juan.Id, draft.TenantId);
        Assert.Equal([Ana.Id], draft.LessorIds);
        Assert.Equal([Raul.Id], draft.CodebtorIds);
        Assert.Equal(100m, Assert.Single(draft.Shares).Percentage);
        Assert.Equal(new DateOnly(2026, 12, 1), draft.StartDate);
        Assert.Equal(450_000m, draft.MonthlyRent);
        Assert.Equal(10, draft.DueDay);
        Assert.Equal(8m, draft.HonorariosPercentage);
        Assert.Null(draft.Clause);
        Assert.Equal(FakeContractRegistry.Saved, viewModel.Created);
        Assert.Null(viewModel.Tenant);
        Assert.Empty(viewModel.Lessors);
        Assert.Equal(string.Empty, viewModel.MonthlyRentText);
        Assert.Equal("10", viewModel.DueDayText);
    }

    /// <summary>Spec Test 12: a refusal names what is missing and keeps the form.</summary>
    [Fact]
    public async Task ARefusal_ShowsItsOwnText_AndKeepsTheForm()
    {
        var port = new FakeContractRegistry { OnCreate = (_, _) => ContractResult.Refused(ContractRefusal.TenantMissing) };
        var viewModel = await FilledAsync(port);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.TenantMissing.OperatorText(), viewModel.RefusalMessage);
        Assert.Null(viewModel.Created);
        Assert.Equal("450.000", viewModel.MonthlyRentText);
        Assert.Single(viewModel.Lessors);
    }

    /// <summary>Spec Test 18: the refusal names who rents the unit.</summary>
    [Fact]
    public async Task AUnitUnderAnActiveContract_NamesTheTenant()
    {
        var port = new FakeContractRegistry { OnCreate = (_, _) => ContractResult.Refused(ContractRefusal.UnitUnderActiveContract, "Juan Pérez") };
        var viewModel = await FilledAsync(port);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains("Juan Pérez", viewModel.RefusalMessage);
    }

    /// <summary>Spec Tests 16, 18, 19: warnings show, write nothing, and accepting sends the same form again.</summary>
    [Fact]
    public async Task Warnings_Show_AndAcceptingProceeds()
    {
        var port = new FakeContractRegistry
        {
            OnCreate = (_, accepted) => accepted
                ? ContractResult.Saved(FakeContractRegistry.Saved)
                : ContractResult.NeedsAccepting([new ContractWarning(ContractWarningKind.NoHonorarios), new ContractWarning(ContractWarningKind.UnitBeingVacated, new DateOnly(2026, 12, 31), "Alem 1258")]),
        };
        var viewModel = await FilledAsync(port);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasWarnings);
        Assert.Equal(2, viewModel.Warnings.Count);
        Assert.Contains("31/12/2026", viewModel.Warnings[1]);
        Assert.True(viewModel.AcceptWarningsCommand.CanExecute(null));
        Assert.Null(viewModel.Created);
        Assert.Equal("450.000", viewModel.MonthlyRentText);

        await viewModel.AcceptWarningsCommand.ExecuteAsync(null);

        Assert.Equal([false, true], port.Created.Select(c => c.Accepted).ToList());
        Assert.False(viewModel.HasWarnings);
        Assert.Equal(FakeContractRegistry.Saved, viewModel.Created);
    }

    [Fact]
    public async Task DecliningTheWarnings_WritesNothing_AndKeepsTheForm()
    {
        var port = new FakeContractRegistry { OnCreate = (_, _) => ContractResult.NeedsAccepting([new ContractWarning(ContractWarningKind.NoHonorarios)]) };
        var viewModel = await FilledAsync(port);
        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.CancelCommand.Execute(null);

        Assert.False(viewModel.HasWarnings);
        Assert.False(viewModel.AcceptWarningsCommand.CanExecute(null));
        Assert.Single(port.Created);
        Assert.Equal("450.000", viewModel.MonthlyRentText);
    }

    /// <summary>What is accepted is what is written, so editing anything on the form withdraws the warnings.</summary>
    [Theory]
    [InlineData("rent")]
    [InlineData("rate")]
    [InlineData("start")]
    [InlineData("tenant")]
    [InlineData("codebtor")]
    [InlineData("unit")]
    [InlineData("share")]
    [InlineData("end")]
    [InlineData("dueDay")]
    [InlineData("cutover")]
    [InlineData("lessor")]
    [InlineData("feeAmount")]
    [InlineData("feeInstalments")]
    [InlineData("clauseInterval")]
    [InlineData("clauseIndex")]
    [InlineData("sharesAsAmounts")]
    public async Task EditingAnythingAfterAWarning_WithdrawsIt(string edit)
    {
        var port = new FakeContractRegistry { OnCreate = (_, _) => ContractResult.NeedsAccepting([new ContractWarning(ContractWarningKind.NoHonorarios)]) };
        var viewModel = await FilledAsync(port);
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasWarnings);

        switch (edit)
        {
            case "rent": viewModel.MonthlyRentText = "500.000"; break;
            case "rate": viewModel.HonorariosText = "6"; break;
            case "start": viewModel.StartDate = new DateOnly(2027, 1, 1); break;
            case "tenant": viewModel.Tenant = Raul; break;
            case "codebtor": viewModel.Codebtors.Add(Raul); break;
            case "unit": viewModel.Units.Add(new ContractFormUnit(Unit("40"))); break;
            case "share": viewModel.Units[0].ShareText = "100"; break;
            case "end": viewModel.NominalEndDate = new DateOnly(2029, 11, 30); break;
            case "dueDay": viewModel.DueDayText = "5"; break;
            case "cutover": viewModel.CutoverDate = new DateOnly(2026, 6, 30); break;
            case "lessor": viewModel.Lessors.Add(Raul); break;
            case "feeAmount": viewModel.FeePlanAmountText = "5.000"; break;
            case "feeInstalments": viewModel.FeePlanInstalmentsText = "6"; break;
            case "clauseInterval": viewModel.ClauseIntervalText = "6"; break;
            case "clauseIndex": viewModel.ClauseIndexIds.Add(Guid.NewGuid()); break;
            case "sharesAsAmounts": viewModel.SharesAsAmounts = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(edit), edit, "No such edit.");
        }

        Assert.False(viewModel.HasWarnings);
        Assert.False(viewModel.AcceptWarningsCommand.CanExecute(null));
        Assert.Single(port.Created);
    }

    /// <summary>Spec Test 22: the form does not offer the cutover to an Empleado, and a date never leaves it.</summary>
    [Fact]
    public async Task AnEmpleado_IsNotOfferedTheCutover_AndNoDateIsSent()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port, admin: false);
        Assert.False(viewModel.IsCutoverOffered);
        viewModel.CutoverDate = new DateOnly(2026, 6, 30);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(port.Created).Draft.CutoverDate);
    }

    /// <summary>Spec Requirement 8: an Admin's date is sent, and the database still decides.</summary>
    [Fact]
    public async Task AnAdmin_IsOfferedTheCutover_AndTheDateIsSent()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port, admin: true);
        Assert.True(viewModel.IsCutoverOffered);
        viewModel.CutoverDate = new DateOnly(2026, 6, 30);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new DateOnly(2026, 6, 30), Assert.Single(port.Created).Draft.CutoverDate);
    }

    /// <summary>Spec Test 19: choosing a lessor suggests the rate, which stays editable.</summary>
    [Fact]
    public async Task ChoosingALessor_SuggestsTheRate_WhichStaysEditable()
    {
        var port = new FakeContractRegistry();
        port.Rates[Ana.Id] = 6m;
        var viewModel = new ContractFormViewModel(port, false);

        await viewModel.AddLessorCommand.ExecuteAsync(Ana);

        Assert.Equal("6", viewModel.HonorariosText);

        viewModel.HonorariosText = "7,5";
        await viewModel.AddLessorCommand.ExecuteAsync(Raul);

        Assert.Equal("7,5", viewModel.HonorariosText);
        Assert.Equal(2, viewModel.Lessors.Count);
    }

    /// <summary>Spec Test 19: a suggestion never overwrites a rate already there, and a lessor with no history suggests nothing.</summary>
    [Fact]
    public async Task ASuggestion_NeverOverwritesARate_AndNoHistorySuggestsNothing()
    {
        var port = new FakeContractRegistry();
        port.Rates[Ana.Id] = 6m;
        var noHistory = new ContractFormViewModel(port, false);
        await noHistory.AddLessorCommand.ExecuteAsync(Raul);
        Assert.Equal(string.Empty, noHistory.HonorariosText);

        var typed = new ContractFormViewModel(port, false) { HonorariosText = "0" };
        await typed.AddLessorCommand.ExecuteAsync(Ana);
        Assert.Equal("0", typed.HonorariosText);
    }

    [Fact]
    public async Task TheSameLessorTwice_IsOneLessor()
    {
        var viewModel = new ContractFormViewModel(new FakeContractRegistry(), false);

        await viewModel.AddLessorCommand.ExecuteAsync(Ana);
        await viewModel.AddLessorCommand.ExecuteAsync(Ana);

        Assert.Single(viewModel.Lessors);
    }

    /// <summary>Spec Test 19: empty stays null and 0 stays 0 on the way to the port.</summary>
    [Theory]
    [InlineData("", null)]
    [InlineData("0", "0")]
    [InlineData("8,5", "8.5")]
    public async Task TheRate_ReachesThePortAsTyped(string typed, string? expected)
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.HonorariosText = typed;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var sent = Assert.Single(port.Created).Draft.HonorariosPercentage;
        Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), sent);
    }

    /// <summary>Spec Test 17: percentages as typed; nothing typed splits equally.</summary>
    [Fact]
    public async Task Shares_ArePercentagesAsTyped_OrEqualWhenNothingIsTyped()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.Units.Add(new ContractFormUnit(Unit("40")));

        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal([50m, 50m], port.Created[0].Draft.Shares.Select(s => s.Percentage).ToList());

        var typed = await FilledAsync(port);
        typed.Units.Add(new ContractFormUnit(Unit("40")));
        typed.Units[0].ShareText = "60";
        typed.Units[1].ShareText = "40";

        await typed.SaveCommand.ExecuteAsync(null);
        Assert.Equal([60m, 40m], port.Created[1].Draft.Shares.Select(s => s.Percentage).ToList());
    }

    /// <summary>Spec Test 17: an amount typed per unit converts to a percentage.</summary>
    [Fact]
    public async Task Shares_TypedAsAmounts_Convert()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.MonthlyRentText = "300.000";
        viewModel.Units.Add(new ContractFormUnit(Unit("40")));
        viewModel.SharesAsAmounts = true;
        viewModel.Units[0].ShareText = "100.000";
        viewModel.Units[1].ShareText = "200.000";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(100m, Assert.Single(port.Created).Draft.Shares.Sum(s => s.Percentage));
        Assert.Equal(66.666667m, port.Created[0].Draft.Shares[1].Percentage);
    }

    /// <summary>Text that is not a number never reaches the port, and says which field.</summary>
    [Theory]
    [InlineData("rent", ContractRefusal.RentNotWholePositive)]
    [InlineData("dueDay", ContractRefusal.DueDayOutOfRange)]
    [InlineData("rate", ContractRefusal.HonorariosOutOfRange)]
    [InlineData("fee", ContractRefusal.FeePlanInvalid)]
    [InlineData("interval", ContractRefusal.ClauseInvalid)]
    [InlineData("share", ContractRefusal.SharesNotHundred)]
    [InlineData("amounts", ContractRefusal.SharesNotHundred)]
    [InlineData("oneBlank", ContractRefusal.SharesNotHundred)]
    public async Task TextThatIsNotANumber_IsRefusedWithoutCallingThePort(string field, ContractRefusal expected)
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        switch (field)
        {
            case "rent": viewModel.MonthlyRentText = "mucho"; break;
            case "dueDay": viewModel.DueDayText = "diez"; break;
            case "rate": viewModel.HonorariosText = "ocho"; break;
            case "fee": viewModel.FeePlanInstalmentsText = "tres"; break;
            case "interval": viewModel.ClauseIndexIds.Add(Guid.NewGuid()); viewModel.ClauseIntervalText = "seis"; break;
            case "share": viewModel.Units[0].ShareText = "todo"; break;
            case "amounts": viewModel.SharesAsAmounts = true; viewModel.Units[0].ShareText = "1"; break;
            default:
                viewModel.Units.Add(new ContractFormUnit(Unit("40")));
                viewModel.Units[0].ShareText = "60";
                break;
        }

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expected.OperatorText(), viewModel.RefusalMessage);
        Assert.Empty(port.Created);
    }

    /// <summary>Amounts convert against the canon, so with no canon the message is about the canon, not the shares.</summary>
    [Fact]
    public async Task AmountsWithNoCanon_SayTheCanonIsMissing()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        viewModel.MonthlyRentText = string.Empty;
        viewModel.SharesAsAmounts = true;
        viewModel.Units[0].ShareText = "100.000";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.RentNotWholePositive.OperatorText(), viewModel.RefusalMessage);
        Assert.Empty(port.Created);
    }

    /// <summary>Warnings are about what was sent, so an edit made while it was out never lets them be accepted for the new form.</summary>
    [Fact]
    public async Task AnEditWhileSaving_DiscardsTheWarningsThatComeBack()
    {
        var gate = new TaskCompletionSource();
        var port = new FakeContractRegistry
        {
            Gate = gate,
            OnCreate = (_, _) => ContractResult.NeedsAccepting([new ContractWarning(ContractWarningKind.NoHonorarios)]),
        };
        var viewModel = await FilledAsync(port);

        var saving = viewModel.SaveCommand.ExecuteAsync(null);
        viewModel.MonthlyRentText = "500.000";
        gate.SetResult();
        await saving;

        Assert.False(viewModel.HasWarnings);
        Assert.False(viewModel.AcceptWarningsCommand.CanExecute(null));
        Assert.Equal(ContractFormViewModel.FormChangedWhileSaving, viewModel.RefusalMessage);
        Assert.Equal("500.000", viewModel.MonthlyRentText);
        Assert.Single(port.Created);
    }

    /// <summary>A contract written stands, and an edit made while it was out is not thrown away without a word.</summary>
    [Fact]
    public async Task AnEditWhileSaving_IsKept_WhenTheContractIsWritten()
    {
        var gate = new TaskCompletionSource();
        var port = new FakeContractRegistry { Gate = gate };
        var viewModel = await FilledAsync(port);

        var saving = viewModel.SaveCommand.ExecuteAsync(null);
        viewModel.MonthlyRentText = "500.000";
        gate.SetResult();
        await saving;

        Assert.Equal(FakeContractRegistry.Saved, viewModel.Created);
        Assert.Equal("500.000", viewModel.MonthlyRentText);
    }

    /// <summary>The clause and the fee plan reach the port as typed.</summary>
    [Fact]
    public async Task TheClauseAndFeePlan_AreSent()
    {
        var port = new FakeContractRegistry();
        var viewModel = await FilledAsync(port);
        var index = Guid.NewGuid();
        viewModel.ClauseIndexIds.Add(index);
        viewModel.ClauseIntervalText = "6";
        viewModel.ClauseOffsetText = "3";
        viewModel.FeePlanAmountText = "120.000";
        viewModel.FeePlanInstalmentsText = "3";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var draft = Assert.Single(port.Created).Draft;
        Assert.Equal(6, draft.Clause!.IntervalMonths);
        Assert.Equal(3, draft.Clause.WindowOffsetMonths);
        Assert.Equal([index], draft.Clause.IndexIds);
        Assert.Equal(120_000m, draft.FeePlanAmount);
        Assert.Equal(3, draft.FeePlanInstalments);
    }

    /// <summary>A write that fails shows the generic text only and keeps what was typed.</summary>
    [Fact]
    public async Task AWriteThatFails_ShowsTheGenericMessage_AndKeepsTheForm()
    {
        var port = new FakeContractRegistry { OnCreate = (_, _) => throw new InvalidOperationException("Host=secret") };
        var viewModel = await FilledAsync(port);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.Generic, viewModel.Runner.ErrorMessage);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal("450.000", viewModel.MonthlyRentText);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    /// <summary>Spec Test 10: the state stays Vigente beside the flag; the search and the state reach the port.</summary>
    [Fact]
    public async Task TheList_FlagsAContractPastItsEnd_WithoutChangingItsState()
    {
        var past = FakeContractRegistry.Saved with { IsPastEnd = true };
        var port = new FakeContractRegistry { Contracts = [past, FakeContractRegistry.Saved] };
        var viewModel = new ContractsViewModel(port) { SearchText = "perez", StatusFilter = ContractStatus.Active };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(("perez", ContractStatus.Active), port.Lists.Single());
        Assert.Equal("Vigente", viewModel.Contracts[0].StatusText);
        Assert.NotNull(viewModel.Contracts[0].PastEndFlag);
        Assert.Null(viewModel.Contracts[1].PastEndFlag);
        Assert.Equal("Pérez, Juan", viewModel.Contracts[0].Tenants);
    }
}
