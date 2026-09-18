using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Assigns a <see cref="Party"/> to a <see cref="Contract"/> under a <see cref="PartyRole"/>.
/// Uniqueness key is (ContractId, PartyId, Role): the same party may hold two different
/// roles on the same contract, but the same party+role pair may not repeat.
/// </summary>
public sealed class ContractParty
{
    public Guid ContractId { get; }
    public Guid PartyId { get; }
    public PartyRole Role { get; }

    public ContractParty(Guid contractId, Guid partyId, PartyRole role)
    {
        ContractId = contractId;
        PartyId = partyId;
        Role = role;
    }
}
