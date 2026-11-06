namespace Inmobiliaria.Domain.Leasing;

/// <summary>What a row of <c>contract_events</c> records (gestion-de-alquileres design D12).</summary>
public enum ContractEventKind
{
    Cancelled,
    NoticeUndone,
    EndUndone,
    PartyRemoved,
}
