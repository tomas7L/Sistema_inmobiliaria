using Inmobiliaria.Domain.Shared;

namespace Inmobiliaria.Domain.Units;

/// <summary>A leasable parking space (cochera).</summary>
public sealed class ParkingUnit : Unit
{
    public ParkingUnit(Guid id, Address address)
        : base(id, UnitType.ParkingSpace, address)
    {
    }

    /// <summary>EF Core materialization constructor — see <see cref="Unit(Guid, UnitType)"/>.</summary>
    private ParkingUnit(Guid id)
        : base(id, UnitType.ParkingSpace)
    {
    }
}
