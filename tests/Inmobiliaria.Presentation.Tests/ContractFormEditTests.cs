using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// The contract form opened on a contract that exists, as the designer's canvas "Editar contrato" draws it (gestion-de-alquileres
/// spec Requirement 9, task 12.3d): it shows the contract as it stands, locks what can no longer be corrected and says why, and a
/// save sends only what changed, each part through its own port call.
/// </summary>
public class ContractFormEditTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly Guid Icl = Guid.NewGuid();

    private static readonly PartyListing Demaria = new(Guid.NewGuid(), "Nicolás", "Demaría", "30123456", null, null, null, null);
    private static readonly PartyListing Ocampo = new(Guid.NewGuid(), "Martín", "Ocampo", "28111222", null, null, null, null);
    private static readonly PartyListing Molina = new(Guid.NewGuid(), "Sofía", "Molina", null, "27222333444", null, null, null);
    private static readonly PartyListing Vera = new(Guid.NewGuid(), "Marta", "Vera", null, null, null, null, null);

    private static readonly UnitListing Alem = new(
        Guid.NewGuid(), UnitType.Property, new Address("Alem", "1258", "Sunchales", "Santa Fe", "2322"),
        new UnitCoverage(ContractStatus.Active, "Ocampo, Martín", null));

    private static readonly UnitListing Belgrano = new(
        Guid.NewGuid(), UnitType.Property, new Address("Belgrano", "40", "Sunchales", "Santa Fe", "2322"), null);

    private static ContractDetail Standing(ContractRefusal? locked = null, ContractStatus status = ContractStatus.Active) => new(
        ContractId, status, new DateOnly(2026, 12, 1), new DateOnly(2028, 11, 30), false, null, null, null,
        status == ContractStatus.Ended ? EndReason.Expiry : null, null, 450_000m, 10, 8m, new ContractualFeePlan(300_000m, 3),
        [
            new ContractPartyLine(Ocampo.Id, "Ocampo, Martín", PartyRole.Tenant),
            new ContractPartyLine(Demaria.Id, "Demaría, Nicolás", PartyRole.Lessor),
            new ContractPartyLine(Molina.Id, "Molina, Sofía", PartyRole.Codebtor),
        ],
        [new ContractUnitLine(Alem.Id, "Alem 1258, Sunchales", 100m)],
        new ContractClauseLine(["ICL"], 6, RoundingRule.TruncateToWholePeso, [Icl], 0),
        [])
    {
        CorrectionLock = locked,
    };

    private static async Task<(ContractFormViewModel Form, FakeContractRegistry Port)> EditingAsync(ContractDetail? detail = null)
    {
        detail ??= Standing();
        var port = new FakeContractRegistry { Detail = detail };
        var form = new ContractFormViewModel(
            port, false, new FakeClock(Today), new FakeIndexRegistry(),
            new FakePartyRegistry { Persons = [Demaria, Ocampo, Molina, Vera] }, new FakeUnitRegistry { Units = [Alem, Belgrano] },
            editing: detail);
        await form.LoadCommand.ExecuteAsync(null);
        return (form, port);
    }

    private static string[] Names(FakeContractRegistry port) => port.Calls.Select(c => (string)c[0]!).ToArray();

    /// <summary>Canvas "Editar contrato": the form opens holding the contract as it stands, and asks nothing to write.</summary>
    [Fact]
    public async Task Opened_ShowsTheContractAsItStands()
    {
        var (form, port) = await EditingAsync();

        Assert.True(form.IsEditing);
        Assert.Equal(Ocampo, form.Tenant);
        Assert.Equal([Demaria], form.Lessors);
        Assert.Equal([Molina], form.Codebtors);
        Assert.Equal(Alem, Assert.Single(form.Units).Unit);
        Assert.Equal(string.Empty, form.Units[0].ShareText);
        Assert.Equal(("01/12/2026", "30/11/2028"), (form.StartDateText, form.NominalEndDateText));
        Assert.Equal(("450.000", "10", "8"), (form.MonthlyRentText, form.DueDayText, form.HonorariosText));
        Assert.True(form.HasFeePlan);
        Assert.Equal(("300.000", "3"), (form.FeePlanAmountText, form.FeePlanInstalmentsText));
        Assert.True(form.HasClause);
        Assert.Equal([Icl], form.ClauseIndexIds);
        Assert.Equal(("6", "0"), (form.ClauseIntervalText, form.ClauseOffsetText));
        Assert.False(form.IsCutoverShown);
        Assert.Empty(port.Calls);
    }

    /// <summary>A contract over two units shows each one's share, so a save that changes nothing sends nothing.</summary>
    [Fact]
    public async Task TwoUnits_ShowEachShare()
    {
        var (form, port) = await EditingAsync(Standing() with
        {
            Units = [new ContractUnitLine(Alem.Id, "Alem 1258, Sunchales", 60m), new ContractUnitLine(Belgrano.Id, "Belgrano 40, Sunchales", 40m)],
        });

        Assert.Equal(["60", "40"], form.Units.Select(u => u.ShareText));

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Empty(port.Calls);
    }

    /// <summary>
    /// Canvas "Editar contrato": with movements the contract's data lock and the banner says why; a confirmed adjustment locks the
    /// clause too. An ended contract keeps only its honorarios and codeudores (Tomás, 2026-11-19).
    /// </summary>
    [Theory]
    [InlineData(null, ContractStatus.Active, false, false)]
    [InlineData(ContractRefusal.AccountHasMovements, ContractStatus.Active, true, false)]
    [InlineData(ContractRefusal.AdjustmentConfirmed, ContractStatus.Active, true, true)]
    [InlineData(ContractRefusal.ContractEnded, ContractStatus.Ended, true, true)]
    public async Task WhatIsLocked_FollowsWhyTheContractCannotBeCorrected(ContractRefusal? locked, ContractStatus status, bool core, bool clause)
    {
        var (form, _) = await EditingAsync(Standing(locked, status));

        Assert.Equal(core, form.IsCoreLocked);
        Assert.Equal(clause, form.IsClauseLocked);
        Assert.Equal(locked == ContractRefusal.ContractEnded ? ContractFormViewModel.EndedLockText : locked?.OperatorText(), form.LockText);
        Assert.Equal(
            locked switch
            {
                ContractRefusal.AdjustmentConfirmed => ContractFormViewModel.ClauseLockedByAdjustment,
                ContractRefusal.ContractEnded => ContractFormViewModel.ClauseLockedByEnd,
                _ => null,
            },
            form.ClauseLockText);
    }

    /// <summary>
    /// Canvas "Editar contrato": a unit is replaced, never added, so the unit picker shows only once one was taken away; locked,
    /// no unit is taken away or picked. A new contract adds and removes freely.
    /// </summary>
    [Fact]
    public async Task TheUnitPicker_ShowsOnlyToReplaceAUnit()
    {
        var (form, _) = await EditingAsync();

        Assert.True(form.CanRemoveUnits);
        Assert.False(form.IsUnitPickerShown);

        form.RemoveUnitCommand.Execute(form.Units[0]);

        Assert.True(form.IsUnitPickerShown);

        var (locked, _) = await EditingAsync(Standing(ContractRefusal.AccountHasMovements));
        Assert.False(locked.CanRemoveUnits);
        Assert.False(locked.IsUnitPickerShown);

        var fresh = new ContractFormViewModel(new FakeContractRegistry(), false, new FakeClock(Today), new FakeIndexRegistry());
        Assert.True(fresh.CanRemoveUnits);
        Assert.True(fresh.IsUnitPickerShown);
    }

    [Fact]
    public void ANewContract_IsNotEditing_AndLocksNothing()
    {
        var form = new ContractFormViewModel(new FakeContractRegistry(), false, new FakeClock(Today), new FakeIndexRegistry());

        Assert.False(form.IsEditing);
        Assert.False(form.IsCoreLocked);
        Assert.False(form.IsClauseLocked);
        Assert.Null(form.LockText);
    }

    [Fact]
    public async Task SavingWithNothingChanged_SendsNothing_AndSaysSo()
    {
        var (form, port) = await EditingAsync();

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Empty(port.Calls);
        Assert.Equal(ContractFormViewModel.NothingChanged, form.RefusalMessage);
        Assert.Null(form.Corrected);
    }

    /// <summary>Canvas "Editar contrato": only the rate changed, so only the rate is sent; saving it again sends nothing more.</summary>
    [Fact]
    public async Task ChangingOnlyTheRate_SendsOnlyIt_ThenNothingMore()
    {
        var (form, port) = await EditingAsync();
        form.HonorariosText = "9";

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal<object?>(["SetRate", ContractId, 9m], Assert.Single(port.Calls));
        Assert.Equal(FakeContractRegistry.Saved, form.Corrected);
        Assert.Equal("El contrato quedó con los datos corregidos.", form.SavedText);

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Single(port.Calls);
        Assert.Equal(ContractFormViewModel.NothingChanged, form.RefusalMessage);
    }

    /// <summary>Each part goes through its own call, the contract's data first, since it is the one most likely refused or warned about.</summary>
    [Fact]
    public async Task EachPartChanged_IsSent_InOrder()
    {
        var (form, port) = await EditingAsync();
        form.MonthlyRentText = "500000";
        form.Codebtors.Clear();
        form.Codebtors.Add(Vera);
        form.HonorariosText = "9";
        form.ClauseIntervalText = "12";

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Correct", "SetCodebtors", "SetRate", "SetClause"], Names(port));
        var correction = Assert.IsType<ContractCorrection>(port.Calls[0][2]);
        Assert.Equal((Ocampo.Id, 500_000m, 10), (correction.TenantId!.Value, correction.MonthlyRent!.Value, correction.DueDay));
        Assert.Equal([Demaria.Id], correction.LessorIds);
        Assert.Equal([new UnitShare(Alem.Id, 100m)], correction.Shares);
        Assert.Equal((new DateOnly(2026, 12, 1), new DateOnly(2028, 11, 30)), (correction.StartDate!.Value, correction.NominalEndDate!.Value));
        Assert.Equal((300_000m, 3), (correction.FeePlanAmount!.Value, correction.FeePlanInstalments!.Value));
        Assert.False((bool)port.Calls[0][3]!);
        Assert.Equal([Vera.Id], Assert.IsAssignableFrom<IReadOnlyList<Guid>>(port.Calls[1][2]));
        var clause = Assert.IsType<ClauseDraft>(port.Calls[3][2]);
        Assert.Equal([Icl], clause.IndexIds);
        Assert.Equal((12, 0), (clause.IntervalMonths, clause.WindowOffsetMonths));
        Assert.NotNull(form.Corrected);
    }

    /// <summary>A replaced unit goes in place of the one it replaces; one more or one less is said under the units, before asking.</summary>
    [Fact]
    public async Task AUnitIsReplaced_NeverAddedOrDropped()
    {
        var (form, port) = await EditingAsync();
        form.Units.Add(new ContractFormUnit(Belgrano));

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.UnitCountChanged.OperatorText(), form.Errors[ContractField.Units]);
        Assert.Empty(port.Calls);

        form.Units.RemoveAt(0);
        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal([new UnitShare(Belgrano.Id, 100m)], Assert.IsType<ContractCorrection>(Assert.Single(port.Calls)[2]).Shares);
    }

    /// <summary>The warnings come back before anything is written, and accepting them sends every part again.</summary>
    [Fact]
    public async Task Warnings_StopEverything_UntilAccepted()
    {
        var (form, port) = await EditingAsync();
        port.OnChange = () => port.Calls[^1] is ["Correct", _, _, false]
            ? ContractResult.NeedsAccepting([new ContractWarning(ContractWarningKind.FirstDueDatePassed, new DateOnly(2026, 11, 10))])
            : ContractResult.Saved(FakeContractRegistry.Saved);
        form.DueDayText = "5";
        form.HonorariosText = "9";

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Correct"], Names(port));
        Assert.True(form.HasWarnings);
        Assert.Null(form.Corrected);

        await form.AcceptWarningsCommand.ExecuteAsync(null);

        Assert.Equal(["Correct", "Correct", "SetRate"], Names(port));
        Assert.True((bool)port.Calls[1][3]!);
        Assert.NotNull(form.Corrected);
    }

    /// <summary>A part refused after another was written says what is already saved, and saving again sends only what is left.</summary>
    [Fact]
    public async Task ARefusalAfterAPartWasSaved_SaysWhatWasSaved_AndSendsOnlyTheRestAgain()
    {
        var (form, port) = await EditingAsync();
        port.OnChange = () => port.Calls[^1][0] is "SetRate"
            ? ContractResult.Refused(ContractRefusal.HonorariosOutOfRange)
            : ContractResult.Saved(FakeContractRegistry.Saved);
        form.Codebtors.Clear();
        form.HonorariosText = "9";

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal($"{ContractRefusal.HonorariosOutOfRange.OperatorText()} Quedó guardado: los codeudores.", form.RefusalMessage);
        Assert.Null(form.Corrected);

        port.OnChange = null;
        port.Calls.Clear();
        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["SetRate"], Names(port));
        Assert.NotNull(form.Corrected);
    }

    /// <summary>
    /// With movements, what is locked is never sent, even if it no longer reads as the contract does: only the honorarios, the
    /// codeudores and the clause are. A confirmed adjustment leaves the clause out too.
    /// </summary>
    [Theory]
    [InlineData(ContractRefusal.AccountHasMovements, new[] { "SetRate", "SetClause" })]
    [InlineData(ContractRefusal.AdjustmentConfirmed, new[] { "SetRate" })]
    public async Task WhatIsLocked_IsNeverSent(ContractRefusal locked, string[] sent)
    {
        var (form, port) = await EditingAsync(Standing(locked));
        form.MonthlyRentText = "1";
        form.HonorariosText = "9";
        form.ClauseIntervalText = "12";

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, form.Errors.Count);
        Assert.Equal(sent, Names(port));
    }

    /// <summary>The port can change a rate and a clause but not take them away: emptying either is said beside it, before asking.</summary>
    [Fact]
    public async Task EmptyingTheRate_OrDroppingTheClause_IsSaidBesideIt()
    {
        var (form, port) = await EditingAsync();
        form.HonorariosText = string.Empty;
        form.HasClause = false;

        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ContractRefusal.HonorariosCannotBeCleared.OperatorText(), form.Errors[ContractField.Honorarios]);
        Assert.Equal(ContractRefusal.ClauseCannotBeRemoved.OperatorText(), form.Errors[ContractField.Clause]);
        Assert.Empty(port.Calls);
    }

    /// <summary>A contract with no rate yet may stay without one, and a contract on a fixed rent may get a clause.</summary>
    [Fact]
    public async Task NoRateYet_StaysEmpty_AndAFixedRentTakesAClause()
    {
        var (form, port) = await EditingAsync(Standing() with { HonorariosPercentage = null, Clause = null });
        Assert.Equal(string.Empty, form.HonorariosText);
        Assert.False(form.HasClause);

        await form.SaveCommand.ExecuteAsync(null);
        Assert.Empty(port.Calls);

        form.HasClause = true;
        form.ClauseIndexIds.Add(Icl);
        form.ClauseOffsetText = "0";
        await form.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["SetClause"], Names(port));
    }
}
