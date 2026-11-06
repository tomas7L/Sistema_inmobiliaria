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

    /// <summary>When the party was taken off the contract, or null while they are on it (gestion-de-alquileres design D13). Ordinary reads never see a removed row.</summary>
    public DateTimeOffset? RemovedAt { get; private set; }

    public ContractParty(Guid contractId, Guid partyId, PartyRole role)
    {
        ContractId = contractId;
        PartyId = partyId;
        Role = role;
    }

    internal void MarkRemoved(DateTimeOffset at) => RemovedAt = at;

    internal void Restore() => RemovedAt = null;
}
