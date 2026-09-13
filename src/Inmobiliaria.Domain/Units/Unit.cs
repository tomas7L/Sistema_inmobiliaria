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

    /// <summary>
    /// EF Core materialization constructor. <see cref="Address"/> is an owned navigation and
    /// cannot be constructor-bound by EF, so this overload leaves it for EF to set via the
    /// property's private setter immediately after construction; it must never be used from
    /// application code.
    /// </summary>
    protected Unit(Guid id, UnitType unitType)
    {
        Id = id;
        UnitType = unitType;
        Address = null!;
    }

    public void Relocate(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        Address = address;
    }
}
