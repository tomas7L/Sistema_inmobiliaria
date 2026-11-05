using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// <see cref="IUnitRegistry"/> over <c>units</c> (gestion-de-alquileres spec Requirements 4 and 5, design §2.2). Availability
/// is derived on each read by one query and written nowhere. Identical addresses are compared as folded text in memory and
/// only warn, so no index refuses them.
/// </summary>
public sealed class UnitRegistry : IUnitRegistry
{
    private readonly InmobiliariaDbContext _context;

    public UnitRegistry(InmobiliariaDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async Task<IReadOnlyList<UnitListing>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _context.Units
            .AsNoTracking()
            .Select(unit => new
            {
                Unit = unit,
                Covering = (
                    from contractUnit in _context.ContractUnits
                    join contract in _context.Contracts on contractUnit.ContractId equals contract.Id
                    where contractUnit.UnitId == unit.Id
                        && (contract.Status == ContractStatus.Active || contract.Status == ContractStatus.PendingTermination)
                    orderby contract.Status == ContractStatus.Active descending, contract.Id
                    select new
                    {
                        contract.Status,
                        contract.PlannedMoveOutDate,
                        Tenant = (
                            from contractParty in _context.ContractParties
                            join party in _context.Parties on contractParty.PartyId equals party.Id
                            where contractParty.ContractId == contract.Id && contractParty.Role == PartyRole.Tenant
                            select new { party.FirstName, party.LastName }).FirstOrDefault(),
                    }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows
            .Select(row => new UnitListing(
                row.Unit.Id,
                row.Unit.UnitType,
                row.Unit.Address,
                row.Covering is null
                    ? null
                    : new UnitCoverage(
                        row.Covering.Status,
                        row.Covering.Tenant is null ? null : $"{row.Covering.Tenant.FirstName} {row.Covering.Tenant.LastName}",
                        row.Covering.PlannedMoveOutDate)))
            .OrderBy(listing => AccountSearch.Fold(listing.AddressText), StringComparer.Ordinal)
            .ThenBy(listing => listing.Id)
            .ToList();
    }

    public async Task<UnitResult> CreateAsync(UnitType type, UnitDraft draft, bool acceptIdenticalAddress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (UnitDraftRules.Check(draft) is { } refusal)
        {
            return UnitResult.Refused(refusal);
        }

        var clean = draft.Normalized();
        if (!acceptIdenticalAddress && await IdenticalToAsync(clean, null, ct) is { } existing)
        {
            return UnitResult.Refused(UnitRefusal.IdenticalAddressNotAccepted, existing);
        }

        Unit unit = type switch
        {
            UnitType.Property => new PropertyUnit(Guid.NewGuid(), AddressOf(clean)),
            UnitType.ParkingSpace => new ParkingUnit(Guid.NewGuid(), AddressOf(clean)),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "A unit type this registry cannot create."),
        };
        _context.Units.Add(unit);
        await _context.SaveChangesAsync(ct);

        return UnitResult.Saved(new UnitListing(unit.Id, unit.UnitType, unit.Address, null));
    }

    public async Task<UnitResult> RelocateAsync(Guid unitId, UnitDraft draft, bool acceptIdenticalAddress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (UnitDraftRules.Check(draft) is { } refusal)
        {
            return UnitResult.Refused(refusal);
        }

        var unit = await _context.Units.SingleOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null)
        {
            return UnitResult.Refused(UnitRefusal.UnitNotFound);
        }

        var clean = draft.Normalized();
        if (!acceptIdenticalAddress && await IdenticalToAsync(clean, unitId, ct) is { } existing)
        {
            return UnitResult.Refused(UnitRefusal.IdenticalAddressNotAccepted, existing);
        }

        unit.Relocate(AddressOf(clean));
        await _context.SaveChangesAsync(ct);

        return UnitResult.Saved((await ListAsync(ct)).Single(listing => listing.Id == unitId));
    }

    private async Task<UnitListing?> IdenticalToAsync(UnitDraft clean, Guid? except, CancellationToken ct) =>
        (await ListAsync(ct)).FirstOrDefault(listing => listing.Id != except && IsIdentical(listing.Address, clean));

    private static bool IsIdentical(Address stored, UnitDraft clean) =>
        Same(stored.Street, clean.Street)
        && Same(stored.Number, clean.Number)
        && Same(stored.Floor, clean.Floor)
        && Same(stored.Apartment, clean.Apartment)
        && Same(stored.City, clean.City)
        && Same(stored.Province, clean.Province)
        && Same(stored.PostalCode, clean.PostalCode);

    private static bool Same(string? stored, string? typed) =>
        AccountSearch.Fold(stored ?? string.Empty) == AccountSearch.Fold(typed ?? string.Empty);

    private static Address AddressOf(UnitDraft clean) =>
        new(clean.Street!, clean.Number!, clean.City!, clean.Province!, clean.PostalCode!, clean.Floor, clean.Apartment);
}
