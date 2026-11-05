using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>
/// A scriptable <see cref="IUnitRegistry"/>: the list it answers with is set by the test, each write is recorded and
/// answered by a delegate the test assigned (success when none is), so a test can prove a call was never made.
/// </summary>
internal sealed class FakeUnitRegistry : IUnitRegistry
{
    public List<UnitListing> Units { get; set; } = [];

    public Func<UnitType, UnitDraft, bool, UnitResult>? OnCreate { get; set; }

    public Func<Guid, UnitDraft, bool, UnitResult>? OnRelocate { get; set; }

    /// <summary>When set, reading the list fails with this, as a dropped connection would.</summary>
    public Exception? ListFailure { get; set; }

    public int Lists { get; private set; }

    public List<(UnitType Type, UnitDraft Draft, bool Accepted)> Created { get; } = [];

    public List<(Guid Id, UnitDraft Draft, bool Accepted)> Relocated { get; } = [];

    public Task<IReadOnlyList<UnitListing>> ListAsync(CancellationToken ct = default)
    {
        Lists++;
        if (ListFailure is { } failure)
        {
            throw failure;
        }

        return Task.FromResult<IReadOnlyList<UnitListing>>(Units.ToList());
    }

    public Task<UnitResult> CreateAsync(UnitType type, UnitDraft draft, bool acceptIdenticalAddress, CancellationToken ct = default)
    {
        Created.Add((type, draft, acceptIdenticalAddress));
        return Task.FromResult(OnCreate?.Invoke(type, draft, acceptIdenticalAddress) ?? UnitResult.Saved(Listing(Guid.NewGuid(), type, draft)));
    }

    public Task<UnitResult> RelocateAsync(Guid unitId, UnitDraft draft, bool acceptIdenticalAddress, CancellationToken ct = default)
    {
        Relocated.Add((unitId, draft, acceptIdenticalAddress));
        return Task.FromResult(OnRelocate?.Invoke(unitId, draft, acceptIdenticalAddress) ?? UnitResult.Saved(Listing(unitId, UnitType.Property, draft)));
    }

    private static UnitListing Listing(Guid id, UnitType type, UnitDraft draft) =>
        new(id, type, new Address("Calle", "1", "Ciudad", "Provincia", "1000", draft.Floor, draft.Apartment), null);
}
