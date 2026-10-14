namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// The memory of one committed payment, as the voiding planner needs it: which period it settled,
/// when the money arrived, and which ledger movements it wrote so a voiding can name them. Plain
/// domain data, not an EF entity yet; a later slice maps the collections table onto it.
/// </summary>
/// <param name="PaymentMovementId">The <see cref="Accounts.MovementKind.Payment"/> this collection wrote.</param>
/// <param name="RecargoChargedMovementId">The <see cref="Accounts.MovementKind.RecargoCharged"/> it wrote, or null when the recargo was zero.</param>
/// <param name="IsAnnulled">True once its recibos were annulled; set by whoever loads the record.</param>
public sealed record CollectionRecord(
    Guid Id,
    Guid AccountId,
    DateOnly Period,
    DateOnly ReceivedOn,
    Guid PaymentMovementId,
    Guid? RecargoChargedMovementId,
    bool IsAnnulled);
