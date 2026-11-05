using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IContractRegistry"/> for the screens: every call builds a <see cref="ContractRegistry"/> over its own
/// context (design D2).
/// </summary>
internal sealed class PerCallContractRegistry : IContractRegistry
{
    private readonly ISessionDbContextFactory _factory;
    private readonly IAgencyClock _clock;

    public PerCallContractRegistry(ISessionDbContextFactory factory, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);

        _factory = factory;
        _clock = clock;
    }

    public Task<IReadOnlyList<ContractListing>> ListAsync(string? text, ContractStatus? status, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ContractRegistry(context, _clock).ListAsync(text, status, ct));

    public Task<ContractDetail?> DetailAsync(Guid contractId, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ContractRegistry(context, _clock).DetailAsync(contractId, ct));

    public Task<ContractResult> CreateAsync(ContractDraft draft, bool acceptWarnings, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ContractRegistry(context, _clock).CreateAsync(draft, acceptWarnings, ct));

    public Task<decimal?> SuggestRateAsync(Guid lessorId, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ContractRegistry(context, _clock).SuggestRateAsync(lessorId, ct));
}
