using Inmobiliaria.Domain.GoLive;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.GoLive;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// ui-de-cobranza spec tests 27 (display half), 30 (before the port) and 31 (design sections 5.3, 6.3), with
/// the port faked.
/// </summary>
public class GoLiveViewModelTests
{
    private static readonly DateOnly Start = new(2025, 3, 1);
    private static readonly DateOnly Fifth = new(2026, 10, 5);

    private static CutoverListing Listing(DateOnly? cutover, CutoverState state, string tenant = "Perez, Maria") =>
        new(Guid.NewGuid(), tenant, "Bv. Belgrano 123, Sunchales", Start, cutover, state);

    private static async Task<GoLiveViewModel> LoadedAsync(FakeGoLiveSetup setup)
    {
        var viewModel = new GoLiveViewModel(setup);
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    /// <summary>Spec test 27, display half: date or none, changeable or locked, and no account.</summary>
    [Fact]
    public async Task TheList_ShowsDateOrNone_ChangeableOrLocked_AndNoAccount()
    {
        var setup = new FakeGoLiveSetup
        {
            Contracts =
            [
                Listing(null, CutoverState.Changeable),
                Listing(Fifth, CutoverState.Changeable),
                Listing(Fifth, CutoverState.Locked),
                Listing(null, CutoverState.NoAccount),
            ],
        };

        var rows = (await LoadedAsync(setup)).Contracts;

        Assert.Equal(
            [
                ("Sin fecha de corte", "Modificable", true),
                ("05/10/2026", "Modificable", true),
                ("05/10/2026", "Bloqueada: la cuenta ya tiene movimientos", false),
                ("Sin fecha de corte", "No se puede cargar: el contrato no tiene cuenta", false),
            ],
            rows.Select(r => (r.CutoverText, r.StateText, r.CanChange)).ToList());
        Assert.All(rows, r => Assert.Equal("Perez, Maria", r.Tenants));
    }

    /// <summary>Spec Requirement 19: not set, set and changeable, set and locked.</summary>
    [Theory]
    [InlineData(null, false, "Sin cargar", true)]
    [InlineData(31001, false, "31001", true)]
    [InlineData(31001, true, "31001", false)]
    public async Task TheStartingNumber_IsShownWithItsState(int? number, bool anyRecibo, string text, bool canChange)
    {
        var setup = new FakeGoLiveSetup { StartingNumber = new StartingNumberState(number, anyRecibo) };

        var viewModel = await LoadedAsync(setup);

        Assert.Equal(text, viewModel.StartingNumberText);
        Assert.Equal(canChange, viewModel.CanChangeStartingNumber);
        Assert.Equal(canChange ? "Modificable" : "Bloqueado: ya se emitió un recibo", viewModel.StartingNumberStateText);
    }

    /// <summary>Spec test 30, before the port: anything that is not a whole number from 1 to int.MaxValue.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("12.5")]
    [InlineData("12,5")]
    [InlineData("treinta")]
    [InlineData("")]
    [InlineData("2147483648")]
    public async Task AnImpossibleStartingNumber_IsRefused_BeforeThePortIsCalled(string typed)
    {
        var setup = new FakeGoLiveSetup();
        var viewModel = await LoadedAsync(setup);
        viewModel.NewStartingNumber = typed;

        await viewModel.SetStartingNumberCommand.ExecuteAsync(null);

        Assert.Empty(setup.StartingNumberCalls);
        Assert.Equal(SetupRefusal.StartingNumberOutOfRange.OperatorText(), viewModel.RefusalMessage);
        Assert.Equal(typed, viewModel.NewStartingNumber);
    }

    [Fact]
    public async Task AValidStartingNumber_ReachesThePort_AndTheStateIsReRead()
    {
        var setup = new FakeGoLiveSetup();
        var viewModel = await LoadedAsync(setup);
        setup.OnSetStartingNumber = n =>
        {
            setup.StartingNumber = new StartingNumberState(n, false);
            return SetupResult.Success;
        };
        viewModel.NewStartingNumber = " 31100 ";

        await viewModel.SetStartingNumberCommand.ExecuteAsync(null);

        Assert.Equal([31100], setup.StartingNumberCalls);
        Assert.Equal("31100", viewModel.StartingNumberText);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal(2, setup.StateCalls);
    }

    [Fact]
    public async Task ACutoverDate_ReachesThePort_AndTheListIsReRead()
    {
        var setup = new FakeGoLiveSetup { Contracts = [Listing(null, CutoverState.Changeable)] };
        var viewModel = await LoadedAsync(setup);
        var row = viewModel.Contracts.Single();
        setup.OnSetCutover = (id, date) =>
        {
            setup.Contracts = [setup.Contracts.Single() with { CutoverDate = date }];
            return SetupResult.Success;
        };
        row.NewCutoverDate = new DateTime(2026, 10, 5);

        await viewModel.SetCutoverDateCommand.ExecuteAsync(row);

        Assert.Equal([(row.ContractId, Fifth)], setup.CutoverCalls);
        Assert.Equal("05/10/2026", viewModel.Contracts.Single().CutoverText);
        Assert.Equal(2, setup.ListCalls);
    }

    /// <summary>A date is required before the port is called; a locked row offers nothing.</summary>
    [Fact]
    public async Task TheCutoverCommand_IsUnavailable_WithoutADate_OrOnARowThatCannotChange()
    {
        var setup = new FakeGoLiveSetup
        {
            Contracts = [Listing(null, CutoverState.Changeable), Listing(null, CutoverState.Locked), Listing(null, CutoverState.NoAccount)],
        };
        var viewModel = await LoadedAsync(setup);
        var (changeable, locked, noAccount) = (viewModel.Contracts[0], viewModel.Contracts[1], viewModel.Contracts[2]);

        Assert.False(viewModel.SetCutoverDateCommand.CanExecute(changeable));
        changeable.NewCutoverDate = new DateTime(2026, 10, 5);
        Assert.True(viewModel.SetCutoverDateCommand.CanExecute(changeable));

        locked.NewCutoverDate = new DateTime(2026, 10, 5);
        noAccount.NewCutoverDate = new DateTime(2026, 10, 5);
        Assert.False(viewModel.SetCutoverDateCommand.CanExecute(locked));
        Assert.False(viewModel.SetCutoverDateCommand.CanExecute(noAccount));
    }

    /// <summary>
    /// Spec test 31: a lock that arrives after the screen showed "changeable" is a refusal for the cutover
    /// date, not an error, and the screen re-reads and shows locked.
    /// </summary>
    [Fact]
    public async Task ALateCutoverLock_IsShownAsARefusal_AndTheRowRefreshesToLocked()
    {
        var setup = new FakeGoLiveSetup { Contracts = [Listing(null, CutoverState.Changeable)] };
        var viewModel = await LoadedAsync(setup);
        var row = viewModel.Contracts.Single();
        setup.OnSetCutover = (_, _) =>
        {
            setup.Contracts = [setup.Contracts.Single() with { State = CutoverState.Locked }];
            return SetupResult.Refused(SetupRefusal.CutoverLocked);
        };
        row.NewCutoverDate = new DateTime(2026, 10, 5);

        await viewModel.SetCutoverDateCommand.ExecuteAsync(row);

        Assert.Equal(SetupRefusal.CutoverLocked.OperatorText(), viewModel.RefusalMessage);
        Assert.Null(viewModel.Runner.ErrorMessage);
        Assert.False(viewModel.Contracts.Single().CanChange);
    }

    /// <summary>Spec test 31: the same for the starting number.</summary>
    [Fact]
    public async Task ALateStartingNumberLock_IsShownAsARefusal_AndTheStateRefreshesToLocked()
    {
        var setup = new FakeGoLiveSetup { StartingNumber = new StartingNumberState(31001, false) };
        var viewModel = await LoadedAsync(setup);
        setup.OnSetStartingNumber = _ =>
        {
            setup.StartingNumber = new StartingNumberState(31001, true);
            return SetupResult.Refused(SetupRefusal.StartingNumberLocked);
        };
        viewModel.NewStartingNumber = "31100";

        await viewModel.SetStartingNumberCommand.ExecuteAsync(null);

        Assert.Equal(SetupRefusal.StartingNumberLocked.OperatorText(), viewModel.RefusalMessage);
        Assert.Null(viewModel.Runner.ErrorMessage);
        Assert.False(viewModel.CanChangeStartingNumber);
    }

    /// <summary>An exception is the generic error path, never a refusal (spec Requirement 4).</summary>
    [Fact]
    public async Task AFailure_IsTheGenericMessage_AndTheTypedValueStays()
    {
        var setup = new FakeGoLiveSetup();
        var viewModel = await LoadedAsync(setup);
        setup.OnSetStartingNumber = _ => throw new InvalidOperationException("Host=db;Password=secret");
        viewModel.NewStartingNumber = "31100";

        await viewModel.SetStartingNumberCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.Generic, viewModel.Runner.ErrorMessage);
        Assert.Null(viewModel.RefusalMessage);
        Assert.Equal("31100", viewModel.NewStartingNumber);
        Assert.False(viewModel.Runner.IsBusy);
    }

    /// <summary>Design D22: the commands are unavailable while an operation runs.</summary>
    [Fact]
    public async Task TheCommands_AreUnavailableWhileBusy()
    {
        var setup = new FakeGoLiveSetup { Contracts = [Listing(null, CutoverState.Changeable)] };
        var viewModel = await LoadedAsync(setup);
        var row = viewModel.Contracts.Single();
        row.NewCutoverDate = new DateTime(2026, 10, 5);
        viewModel.NewStartingNumber = "31001";

        var gate = new TaskCompletionSource();
        setup.BeforeEveryCall = () => gate.Task;
        var running = viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.Runner.IsBusy);
        Assert.False(viewModel.SetCutoverDateCommand.CanExecute(row));
        Assert.False(viewModel.SetStartingNumberCommand.CanExecute(null));
        Assert.False(viewModel.LoadCommand.CanExecute(null));

        gate.SetResult();
        await running;

        // The reload built fresh rows; the commands are back for them.
        var fresh = viewModel.Contracts.Single();
        fresh.NewCutoverDate = new DateTime(2026, 10, 5);
        Assert.True(viewModel.SetCutoverDateCommand.CanExecute(fresh));
        Assert.True(viewModel.SetStartingNumberCommand.CanExecute(null));
    }
}
