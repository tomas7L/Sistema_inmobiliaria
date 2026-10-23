using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.GoLive;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IGoLiveSetup"/> for the screens: every call builds a <see cref="GoLiveSetup"/> over its own
/// context (design D2).
/// </summary>
internal sealed class PerCallGoLiveSetup : IGoLiveSetup
{
    private readonly ISessionDbContextFactory _factory;
    private readonly IAgencyClock _clock;

    public PerCallGoLiveSetup(ISessionDbContextFactory factory, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);

        _factory = factory;
        _clock = clock;
    }

    public Task<IReadOnlyList<CutoverListing>> ListContractsAsync(CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new GoLiveSetup(context, _clock).ListContractsAsync(ct));

    public Task<SetupResult> SetCutoverDateAsync(Guid contractId, DateOnly cutoverDate, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new GoLiveSetup(context, _clock).SetCutoverDateAsync(contractId, cutoverDate, ct));

    public Task<StartingNumberState> GetStartingNumberStateAsync(CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new GoLiveSetup(context, _clock).GetStartingNumberStateAsync(ct));

    public Task<SetupResult> SetStartingNumberAsync(int startingNumber, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new GoLiveSetup(context, _clock).SetStartingNumberAsync(startingNumber, ct));
}
