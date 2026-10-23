using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Presentation.CashDesk;

/// <summary>
/// The pending rent adjustment warning (design section 7.3, D9): one line per missing index value, naming the
/// index and the period, or one line saying the adjustment is ready to confirm. It never blocks the payment
/// (Decision 39). Built from the one due-adjustment query, filtered to the chosen contract.
/// </summary>
public static class AdjustmentWarning
{
    public const string ReadyToConfirm = "Hay un ajuste de alquiler listo para confirmar.";

    public static IReadOnlyList<string> For(IEnumerable<DueAdjustment> due, Guid contractId)
    {
        ArgumentNullException.ThrowIfNull(due);

        var warnings = new List<string>();
        foreach (var adjustment in due.Where(d => d.ContractId == contractId))
        {
            if (adjustment.Missing.Count == 0)
            {
                warnings.Add(ReadyToConfirm);
                continue;
            }

            warnings.AddRange(adjustment.Missing.Select(m =>
                $"Hay un ajuste de alquiler pendiente: falta el índice {m.IndexName} de {m.Period.Month:00}/{m.Period.Year}."));
        }

        return warnings;
    }
}
