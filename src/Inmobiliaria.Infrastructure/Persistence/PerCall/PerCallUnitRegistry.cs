using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IUnitRegistry"/> for the screens: every call builds a <see cref="UnitRegistry"/> over its own
/// context (design D2).
/// </summary>
internal sealed class PerCallUnitRegistry : IUnitRegistry
{
    private readonly ISessionDbContextFactory _factory;

    public PerCallUnitRegistry(ISessionDbContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _factory = factory;
    }

    public Task<IReadOnlyList<UnitListing>> ListAsync(CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new UnitRegistry(context).ListAsync(ct));

    public Task<UnitResult> CreateAsync(UnitType type, UnitDraft draft, bool acceptIdenticalAddress, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new UnitRegistry(context).CreateAsync(type, draft, acceptIdenticalAddress, ct));

    public Task<UnitResult> RelocateAsync(Guid unitId, UnitDraft draft, bool acceptIdenticalAddress, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new UnitRegistry(context).RelocateAsync(unitId, draft, acceptIdenticalAddress, ct));
}
