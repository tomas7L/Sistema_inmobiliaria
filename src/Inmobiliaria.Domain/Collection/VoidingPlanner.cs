using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Collection;

/// <summary>Why a voiding cannot go ahead (design section 7.5, spec Decision 45).</summary>
public enum VoidingRefusal
{
    ReasonRequired,

    /// <summary>The pair is already annulled, or the payment is already reversed on the ledger.</summary>
    AlreadyAnnulled,

    /// <summary>Days 8 to 30 after the payment was recorded: an Empleado may not void.</summary>
    AdminOnly,

    /// <summary>Day 31 on: nobody may void.</summary>
    WindowClosed,

    /// <summary>The voiding's fresh plan differs from the preview the operator confirmed (design D21, applied to voiding).</summary>
    PreviewStale,
}

/// <summary>The operator-facing text of each voiding refusal. Spanish, because the operator reads it.</summary>
public static class VoidingRefusalText
{
    public static string OperatorText(this VoidingRefusal refusal) => refusal switch
    {
        VoidingRefusal.ReasonRequired =>
            "Debe indicar el motivo de la anulación.",
        VoidingRefusal.AlreadyAnnulled =>
            "El cobro ya está anulado.",
        VoidingRefusal.AdminOnly =>
            $"Pasados {CollectionRules.VoidingFreeWindowDays} días, sólo un Administrador puede anular el cobro.",
        VoidingRefusal.WindowClosed =>
            $"Pasados {CollectionRules.VoidingAdminWindowDays} días, el cobro ya no puede anularse.",
        VoidingRefusal.PreviewStale =>
            "La cuenta cambió desde la vista previa. Genere una nueva vista previa.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };
}

/// <summary>
/// What voiding a payment would do. A refused preview carries no annulments and no corrections.
/// </summary>
/// <param name="Annulments">One per recibo of the pair: annulling either annuls both (spec Decision 19).</param>
/// <param name="Corrections">The reversals to append, each naming the movement it reverses, in full.</param>
/// <param name="ResultingAmountOwed">What the account would owe on the voiding day once the corrections are in.</param>
/// <param name="ActingRole">The role the plan was made for. Carried so that the desk can plan again, inside its transaction, for the same role.</param>
public sealed record VoidingPreview(
    DateOnly Period,
    IReadOnlyList<ReciboAnnulment> Annulments,
    IReadOnlyList<AccountMovement> Corrections,
    decimal ResultingAmountOwed,
    VoidingRefusal? Refusal,
    UserRole ActingRole)
{
    public bool CanCommit => Refusal is null;
}

/// <summary>
/// The pure half of voiding a payment: it checks the reason, the state and the window, and offers the
/// two annulments and the corrections. Nothing is appended here; the desk does it in a transaction.
///
/// It writes FULL reversals only: every correction is exactly the negative of the movement it names
/// and the signature takes no amount. That matters because a ledger treats any correction that names
/// a payment as voiding it (<see cref="ContractAccount.HasLivePaymentFor"/>), so a partial correction
/// would silently void the payment while leaving most of it counted.
/// </summary>
public static class VoidingPlanner
{
    public static VoidingPreview Plan(
        ContractAccount account,
        CollectionRecord collection,
        IReadOnlyList<Recibo> pair,
        string reason,
        DateOnly today,
        RecargoTerms terms,
        UserRole actingRole,
        DateOnly recordedOn)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(terms);

        // Everything below up to the refusals is a caller bug, not an operator mistake.
        if (collection.AccountId != account.Id)
        {
            throw new ArgumentException("The collection belongs to another account.", nameof(collection));
        }

        var payment = account.Movements.SingleOrDefault(m => m.Id == collection.PaymentMovementId && m.Kind == MovementKind.Payment)
            ?? throw new ArgumentException("The account holds no such payment.", nameof(collection));

        var recargo = collection.RecargoChargedMovementId is { } recargoId
            ? account.Movements.SingleOrDefault(m => m.Id == recargoId && m.Kind == MovementKind.RecargoCharged)
                ?? throw new ArgumentException("The account holds no such charged recargo.", nameof(collection))
            : null;

        if (pair.Count != 2 ||
            pair.Any(r => r is null || r.Period != collection.Period || r.ReceivedOn != collection.ReceivedOn) ||
            pair.Select(r => r.Copy).Distinct().Count() != 2)
        {
            throw new ArgumentException(
                "A voiding names the two recibos of the payment: one locatario and one propietario.", nameof(pair));
        }

        var age = today.DayNumber - recordedOn.DayNumber;
        ArgumentOutOfRangeException.ThrowIfNegative(age, nameof(recordedOn));

        VoidingPreview Refused(VoidingRefusal refusal) =>
            new(collection.Period, [], [], account.AmountOwed(today, terms), refusal, actingRole);

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Refused(VoidingRefusal.ReasonRequired);
        }

        var alreadyReversed = account.Movements.Any(m =>
            m.Kind == MovementKind.Correction && m.CorrectsMovementId == payment.Id);

        if (collection.IsAnnulled || alreadyReversed)
        {
            return Refused(VoidingRefusal.AlreadyAnnulled);
        }

        if (age > CollectionRules.VoidingAdminWindowDays)
        {
            return Refused(VoidingRefusal.WindowClosed);
        }

        if (age > CollectionRules.VoidingFreeWindowDays && actingRole != UserRole.Admin)
        {
            return Refused(VoidingRefusal.AdminOnly);
        }

        // Each reversal is dated by the ledger date rule (D2): never before the movement it reverses.
        var corrections = new List<AccountMovement>
        {
            AccountMovement.Correction(
                Guid.NewGuid(), -payment.Amount, LedgerDating.CorrectionDate(today, payment.OccurredOn), payment.Id, collection.Period),
        };

        if (recargo is not null)
        {
            corrections.Add(AccountMovement.Correction(
                Guid.NewGuid(), -recargo.Amount, LedgerDating.CorrectionDate(today, recargo.OccurredOn), recargo.Id, collection.Period));
        }

        var annulments = pair.Select(r => new ReciboAnnulment(r.Number, reason)).ToArray();

        return new VoidingPreview(
            collection.Period, annulments, corrections, AmountOwedAfter(account, corrections, today, terms), null, actingRole);
    }

    /// <summary>The amount owed on <paramref name="asOf"/> once the corrections are in, on a copy: the account is left untouched.</summary>
    private static decimal AmountOwedAfter(
        ContractAccount account, IEnumerable<AccountMovement> corrections, DateOnly asOf, RecargoTerms terms)
    {
        var replay = new ContractAccount(account.Id, account.ContractId);

        foreach (var movement in account.Movements.Concat(corrections))
        {
            replay.Append(movement);
        }

        return replay.AmountOwed(asOf, terms);
    }
}
