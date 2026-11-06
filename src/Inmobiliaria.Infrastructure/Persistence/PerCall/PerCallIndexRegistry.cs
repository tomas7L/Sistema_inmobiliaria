using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IIndexRegistry"/> for the screens: every call builds an <see cref="IndexRegistry"/> over its own
/// context (design D2).
/// </summary>
internal sealed class PerCallIndexRegistry : IIndexRegistry
{
    private readonly ISessionDbContextFactory _factory;
    private readonly IAgencyClock _clock;

    public PerCallIndexRegistry(ISessionDbContextFactory factory, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);

        _factory = factory;
        _clock = clock;
    }

    public Task<IReadOnlyList<IndexListing>> ListAsync(CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new IndexRegistry(context, _clock).ListAsync(ct));

    public Task<IndexEntryResult> AddAsync(Guid indexId, IndexPeriod period, decimal level, bool acceptFarLevel, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new IndexRegistry(context, _clock).AddAsync(indexId, period, level, acceptFarLevel, ct));

    public Task<IndexEntryResult> CorrectAsync(Guid valueId, decimal newLevel, bool acceptFarLevel, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new IndexRegistry(context, _clock).CorrectAsync(valueId, newLevel, acceptFarLevel, ct));

    public Task<IReadOnlyList<AffectedAdjustment>> ListAffectedAdjustmentsAsync(Guid valueId, decimal newLevel, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new IndexRegistry(context, _clock).ListAffectedAdjustmentsAsync(valueId, newLevel, ct));

    public Task<IReadOnlyList<IndexCorrectionRecord>> ListCorrectionsAsync(Guid valueId, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new IndexRegistry(context, _clock).ListCorrectionsAsync(valueId, ct));
}
