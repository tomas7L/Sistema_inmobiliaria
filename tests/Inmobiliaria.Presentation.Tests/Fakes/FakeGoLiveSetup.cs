using Inmobiliaria.Domain.GoLive;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>
/// A scriptable <see cref="IGoLiveSetup"/>: the lists it answers with are set by the test, and each set
/// call is counted and answered by a delegate the test assigned (success when none is), so a test can
/// prove a call was never made.
/// </summary>
internal sealed class FakeGoLiveSetup : IGoLiveSetup
{
    public List<CutoverListing> Contracts { get; set; } = [];

    public StartingNumberState StartingNumber { get; set; } = new(null, false);

    public Func<Guid, DateOnly, SetupResult>? OnSetCutover { get; set; }

    public Func<int, SetupResult>? OnSetStartingNumber { get; set; }

    public Func<Task>? BeforeEveryCall { get; set; }

    public List<(Guid ContractId, DateOnly Date)> CutoverCalls { get; } = [];

    public List<int> StartingNumberCalls { get; } = [];

    public int ListCalls { get; private set; }

    public int StateCalls { get; private set; }

    public async Task<IReadOnlyList<CutoverListing>> ListContractsAsync(CancellationToken ct = default)
    {
        await Before();
        ListCalls++;
        return Contracts.ToList();
    }

    public async Task<SetupResult> SetCutoverDateAsync(Guid contractId, DateOnly cutoverDate, CancellationToken ct = default)
    {
        await Before();
        CutoverCalls.Add((contractId, cutoverDate));
        return OnSetCutover?.Invoke(contractId, cutoverDate) ?? SetupResult.Success;
    }

    public async Task<StartingNumberState> GetStartingNumberStateAsync(CancellationToken ct = default)
    {
        await Before();
        StateCalls++;
        return StartingNumber;
    }

    public async Task<SetupResult> SetStartingNumberAsync(int startingNumber, CancellationToken ct = default)
    {
        await Before();
        StartingNumberCalls.Add(startingNumber);
        return OnSetStartingNumber?.Invoke(startingNumber) ?? SetupResult.Success;
    }

    private Task Before() => BeforeEveryCall?.Invoke() ?? Task.CompletedTask;
}
