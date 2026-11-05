using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Shared;

namespace Inmobiliaria.Domain.Units;

/// <summary>
/// What the Propiedades screen and the contract form do with units (gestion-de-alquileres spec Requirements 4 and 5,
/// design §2.2): list them with their availability, create one and correct its address. A unit's type cannot change, so
/// nothing here takes one after creation. Anything an operator can cause comes back as a <see cref="UnitRefusal"/>.
/// </summary>
public interface IUnitRegistry
{
    /// <summary>Every unit by address. Availability is computed on this read and is stored nowhere.</summary>
    Task<IReadOnlyList<UnitListing>> ListAsync(CancellationToken ct = default);

    /// <param name="type">The unit's type, fixed from here on.</param>
    /// <param name="draft">The address.</param>
    /// <param name="acceptIdenticalAddress">The operator saw that another unit has this address and wants to proceed.</param>
    /// <param name="ct">Cancels the write.</param>
    Task<UnitResult> CreateAsync(UnitType type, UnitDraft draft, bool acceptIdenticalAddress, CancellationToken ct = default);

    /// <summary>Corrects the address of a unit, whether or not a contract covers it.</summary>
    /// <param name="unitId">The unit.</param>
    /// <param name="draft">The address.</param>
    /// <param name="acceptIdenticalAddress">As in <see cref="CreateAsync"/>. The unit's own address does not count.</param>
    /// <param name="ct">Cancels the write.</param>
    Task<UnitResult> RelocateAsync(Guid unitId, UnitDraft draft, bool acceptIdenticalAddress, CancellationToken ct = default);
}

/// <param name="Id">The unit.</param>
/// <param name="Type">Property or parking space.</param>
/// <param name="Address">Its address.</param>
/// <param name="Covering">The contract that makes it unavailable, or null when it is available.</param>
public sealed record UnitListing(Guid Id, UnitType Type, Address Address, UnitCoverage? Covering)
{
    /// <summary>Available when, and only when, no <c>Active</c> or <c>PendingTermination</c> contract covers it.</summary>
    public bool IsAvailable => Covering is null;

    public string AddressText
    {
        get
        {
            var floor = Address.Floor is { } f ? $", piso {f}" : string.Empty;
            var apartment = Address.Apartment is { } a ? $", depto {a}" : string.Empty;

            return $"{Address.Street} {Address.Number}{floor}{apartment}, {Address.City}";
        }
    }
}

/// <summary>
/// The contract covering a unit. When a contract waiting for its move-out and its successor both cover the unit, this is
/// the <c>Active</c> one, since that is what a new contract is refused for (spec Decision 30).
/// </summary>
/// <param name="Status"><c>Active</c> or <c>PendingTermination</c>.</param>
/// <param name="TenantName">The tenant's full name, or null when the contract has none.</param>
/// <param name="MoveOutDate">The planned move-out, known once notice is given.</param>
public sealed record UnitCoverage(ContractStatus Status, string? TenantName, DateOnly? MoveOutDate);

public enum UnitRefusal
{
    /// <summary>Street, number, city, province or postal code is missing.</summary>
    AddressIncomplete,

    /// <summary>Another unit has this address and the operator has not accepted it yet. A warning, never a final refusal.</summary>
    IdenticalAddressNotAccepted,

    /// <summary>The unit being corrected is no longer there.</summary>
    UnitNotFound,
}

/// <param name="Refusal">Why nothing was written, or null on success.</param>
/// <param name="Unit">The unit written, on success.</param>
/// <param name="Existing">The other unit at this address, on <see cref="UnitRefusal.IdenticalAddressNotAccepted"/>.</param>
public sealed record UnitResult(UnitRefusal? Refusal, UnitListing? Unit, UnitListing? Existing)
{
    public bool Succeeded => Refusal is null;

    public static UnitResult Saved(UnitListing unit) => new(null, unit, null);

    public static UnitResult Refused(UnitRefusal refusal, UnitListing? existing = null) => new(refusal, null, existing);
}

public static class UnitRefusalText
{
    /// <param name="refusal">The refusal.</param>
    /// <param name="existingAddress">The other unit's address, named by the identical-address warning.</param>
    public static string OperatorText(this UnitRefusal refusal, string? existingAddress = null) => refusal switch
    {
        UnitRefusal.AddressIncomplete =>
            "Escriba la calle, el número, la ciudad, la provincia y el código postal.",
        UnitRefusal.IdenticalAddressNotAccepted =>
            $"Ya existe una unidad en {existingAddress ?? "esa dirección"}. Confirme si quiere guardarla igual.",
        UnitRefusal.UnitNotFound =>
            "La unidad ya no existe. Actualice la lista.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };
}
