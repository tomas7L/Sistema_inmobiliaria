using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Collection;

/// <param name="PaymentMovementId">The payment on the ledger.</param>
/// <param name="RecordedOn">When it was recorded, agency-local: what its voiding window is measured from.</param>
/// <param name="ReciboNumbers">The recibos it issued.</param>
/// <param name="IsAnnulled">Whether it was voided already.</param>
public sealed record CancellationCollection(Guid PaymentMovementId, DateOnly RecordedOn, IReadOnlyList<int> ReciboNumbers, bool IsAnnulled);

/// <param name="Corrections">What to write. Empty on a refusal.</param>
/// <param name="RetainedAmount">Money past the voiding window, which stays with its recibos.</param>
/// <param name="RetainedRecibos">The recibos of that money.</param>
/// <param name="RecibosToVoid">On <see cref="ContractRefusal.PaymentMustBeVoidedFirst"/>, the recibos to void first.</param>
/// <param name="OnlyAnAdminCanVoid">Whether voiding them needs an Admin.</param>
/// <param name="Refusal">Why it cannot be cancelled, or null.</param>
public sealed record ContractCancellationPlan(
    IReadOnlyList<AccountMovement> Corrections,
    decimal RetainedAmount,
    IReadOnlyList<int> RetainedRecibos,
    IReadOnlyList<int> RecibosToVoid,
    bool OnlyAnAdminCanVoid,
    ContractRefusal? Refusal);

/// <summary>
/// The pure half of cancelling a contract loaded by mistake (gestion-de-alquileres spec Requirement 23, design §2.4), like
/// <see cref="VoidingPlanner"/>. Nothing is appended here. A period holding a live payment nets to zero, so it is left alone, and
/// corrections never name a payment, so <see cref="ContractAccount.HasLivePaymentFor"/> still says it stands.
/// </summary>
public static class ContractCancellation
{
    public static ContractCancellationPlan Plan(
        ContractAccount account,
        Contract contract,
        IReadOnlyList<CancellationCollection> collections,
        string? reason,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(collections);

        if (contract.EndReason == EndReason.LoadedByMistake)
        {
            return Refused(ContractRefusal.ContractCancelled);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Refused(ContractRefusal.ReasonMissing);
        }

        var live = collections.Where(c => !c.IsAnnulled).ToList();

        // Measured as voiding measures it. A negative age cannot come from one clock, and reads as just recorded.
        VoidingWindow WindowOf(CancellationCollection collection) =>
            CollectionRules.VoidingWindowOf(Math.Max(0, today.DayNumber - collection.RecordedOn.DayNumber));

        var voidable = live.Where(c => WindowOf(c) != VoidingWindow.Closed).ToList();
        if (voidable.Count > 0)
        {
            return new ContractCancellationPlan(
                [],
                0m,
                [],
                voidable.SelectMany(c => c.ReciboNumbers).Order().ToList(),
                voidable.Any(c => WindowOf(c) == VoidingWindow.AdminOnly),
                ContractRefusal.PaymentMustBeVoidedFirst);
        }

        return new ContractCancellationPlan(
            Corrections(account, today),
            -account.Movements.Where(m => live.Any(c => c.PaymentMovementId == m.Id)).Sum(m => m.Amount),
            live.SelectMany(c => c.ReciboNumbers).Order().ToList(),
            [],
            false,
            null);
    }

    private static ContractCancellationPlan Refused(ContractRefusal refusal) => new([], 0m, [], [], false, refusal);

    private static List<AccountMovement> Corrections(ContractAccount account, DateOnly today)
    {
        var corrections = new List<AccountMovement>();

        decimal NetOf(AccountMovement movement) =>
            movement.Amount + account.Movements.Where(m => m.CorrectsMovementId == movement.Id).Sum(m => m.Amount);

        void Reverse(AccountMovement movement, decimal amount) =>
            corrections.Add(AccountMovement.Correction(
                Guid.NewGuid(), -amount, LedgerDating.CorrectionDate(today, movement.OccurredOn), movement.Id, movement.Period));

        foreach (var period in account.Movements.Where(m => m.Period is not null).Select(m => m.Period!.Value).Distinct().Order())
        {
            // The month goes to zero and no further: a whole charge reversed on a part-paid month would leave a credit.
            var owed = account.PeriodNet(period);
            var charges = account.Movements
                .Where(m => m.Period == period && IsCharge(m.Kind))
                .OrderBy(m => m.OccurredOn)
                .ThenBy(m => m.Kind);

            foreach (var charge in charges)
            {
                var amount = Math.Min(NetOf(charge), owed);
                if (amount > 0m)
                {
                    Reverse(charge, amount);
                    owed -= amount;
                }
            }
        }

        // A frozen surcharge belongs to the account, not to one month.
        foreach (var frozen in account.Movements.Where(m => m.Kind == MovementKind.RecargoFrozen && m.Period is null))
        {
            var amount = NetOf(frozen);
            if (amount > 0m)
            {
                Reverse(frozen, amount);
            }
        }

        return corrections;
    }

    private static bool IsCharge(MovementKind kind) =>
        kind is MovementKind.RentAccrual or MovementKind.OpeningBalance or MovementKind.ContractualFeeInstalment or MovementKind.RecargoCharged
            or MovementKind.AdjustmentDifference;
}
