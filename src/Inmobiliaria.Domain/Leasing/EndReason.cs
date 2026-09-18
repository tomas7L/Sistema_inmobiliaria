namespace Inmobiliaria.Domain.Leasing;

/// <summary>Why a <see cref="Contract"/> transitioned to <see cref="ContractStatus.Ended"/>.</summary>
public enum EndReason
{
    Expiry,
    EarlyTerminationByTenant,
    TerminationForCause,
    MutualAgreement
}
