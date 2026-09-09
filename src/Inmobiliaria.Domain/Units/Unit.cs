using Inmobiliaria.Domain.Shared;

namespace Inmobiliaria.Domain.Units;

/// <summary>
/// The leasable physical asset — address and physical identity only. Availability and
/// every tenancy-derived fact (current tenant, price, dates) are computed from
/// <see cref="Domain.Leasing.Contract"/> state elsewhere and MUST NOT be stored here.
/// </summary>
public abstract class Unit
{
    public Guid Id { get; }
    public UnitType UnitType { get; }
    public Address Address { get; private set; }

    protected Unit(Guid id, UnitType unitType, Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        Id = id;
        UnitType = unitType;
        Address = address;
    }

    public void Relocate(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        Address = address;
    }
}
