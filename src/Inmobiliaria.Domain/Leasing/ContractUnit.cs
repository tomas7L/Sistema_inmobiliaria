namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Assigns a <see cref="Domain.Units.Unit"/> to a <see cref="Contract"/> with its stored
/// rent share, expressed as a percentage (never a fixed amount, so it survives a canon
/// change). Uniqueness key is (ContractId, UnitId). Constructed only through
/// <see cref="Contract.SetUnitShares"/>, which enforces the sum-to-100% invariant.
/// </summary>
public sealed class ContractUnit
{
    public Guid ContractId { get; }
    public Guid UnitId { get; }
    public decimal SharePercentage { get; private set; }

    internal ContractUnit(Guid contractId, Guid unitId, decimal sharePercentage)
    {
        if (sharePercentage <= 0m)
        {
            throw new RentSplitInvariantException("A unit share must be greater than 0%.");
        }

        ContractId = contractId;
        UnitId = unitId;
        SharePercentage = sharePercentage;
    }
}
