using Inmobiliaria.Domain.Shared;

namespace Inmobiliaria.Domain.Units;

/// <summary>A leasable house or apartment.</summary>
public sealed class PropertyUnit : Unit
{
    public PropertyUnit(Guid id, Address address)
        : base(id, UnitType.Property, address)
    {
    }
}
