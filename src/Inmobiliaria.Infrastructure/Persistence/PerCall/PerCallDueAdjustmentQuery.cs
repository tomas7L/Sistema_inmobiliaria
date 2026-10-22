using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IDueAdjustmentQuery"/> for the screens: every call builds a <see cref="DueAdjustmentQuery"/>
/// over its own context (design D2).
/// </summary>
internal sealed class PerCallDueAdjustmentQuery : IDueAdjustmentQuery
{
    private readonly ISessionDbContextFactory _factory;

    public PerCallDueAdjustmentQuery(ISessionDbContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _factory = factory;
    }

    public Task<IReadOnlyList<DueAdjustment>> GetDueAsync(DateOnly asOf, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new DueAdjustmentQuery(context).GetDueAsync(asOf, ct));
}
