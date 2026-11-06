namespace Inmobiliaria.Domain.Leasing;

/// <summary>Why a <see cref="Contract"/> transitioned to <see cref="ContractStatus.Ended"/>.</summary>
public enum EndReason
{
    Expiry,
    EarlyTerminationByTenant,
    TerminationForCause,
    MutualAgreement,

    /// <summary>"Cargado por error". Only <see cref="Contract.Cancel"/> sets it, and the schedule then produces nothing (gestion-de-alquileres design D9).</summary>
    LoadedByMistake,
}
