using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IReciboReader"/> for the screens: every call builds a <see cref="ReciboReader"/> over its own
/// context (design D2).
/// </summary>
internal sealed class PerCallReciboReader : IReciboReader
{
    private readonly ISessionDbContextFactory _factory;

    public PerCallReciboReader(ISessionDbContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _factory = factory;
    }

    public Task<IssuedPayment?> FindByNumberAsync(long reciboNumber, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ReciboReader(context).FindByNumberAsync(reciboNumber, ct));

    public Task<IssuedPayment?> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ReciboReader(context).GetByCollectionAsync(collectionId, ct));

    public Task<IReadOnlyList<RecentCollection>> RecentAsync(int limit, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ReciboReader(context).RecentAsync(limit, ct));
}
