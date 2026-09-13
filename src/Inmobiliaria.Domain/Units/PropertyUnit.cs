using Inmobiliaria.Domain.Shared;

namespace Inmobiliaria.Domain.Units;

/// <summary>A leasable house or apartment.</summary>
public sealed class PropertyUnit : Unit
{
    public PropertyUnit(Guid id, Address address)
        : base(id, UnitType.Property, address)
    {
    }

    /// <summary>EF Core materialization constructor — see <see cref="Unit(Guid, UnitType)"/>.</summary>
    private PropertyUnit(Guid id)
        : base(id, UnitType.Property)
    {
    }
}
