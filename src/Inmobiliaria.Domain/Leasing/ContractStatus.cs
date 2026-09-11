namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Lifecycle state of a <see cref="Contract"/>. There is intentionally no Draft/
/// pending-signature state: a lease is signed on paper and recorded afterward.
/// </summary>
public enum ContractStatus
{
    Active,
    PendingTermination,
    Ended
}
