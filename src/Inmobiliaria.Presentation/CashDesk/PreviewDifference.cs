using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Recibos;

namespace Inmobiliaria.Presentation.CashDesk;

/// <summary>One thing that changed between the preview the operator confirmed and the fresh one.</summary>
public sealed record DifferenceLine(string What, string Before, string After);

/// <summary>
/// What a stale or concurrent refusal shows (spec Requirement 10, design section 7.2): the period, the received
/// date, the three figures, and each recibo line whose amount changed, appeared or disappeared, matched by
/// copy and kind. Pure, so it is tested on its own.
/// </summary>
public static class PreviewDifference
{
    private const string None = "—";

    public static IReadOnlyList<DifferenceLine> Between(CollectionPreview confirmed, CollectionPreview fresh)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(fresh);

        var lines = new List<DifferenceLine>();

        void Compare(string what, string before, string after)
        {
            if (before != after)
            {
                lines.Add(new DifferenceLine(what, before, after));
            }
        }

        Compare("Período", PeriodOf(confirmed), PeriodOf(fresh));
        Compare("Fecha de cobro", Formats.Date(confirmed.ReceivedOn), Formats.Date(fresh.ReceivedOn));
        Compare("Saldo anterior", Formats.Money(confirmed.PreviousAmountOwed), Formats.Money(fresh.PreviousAmountOwed));
        Compare(DeskTexts.LedgerFigureLabel, Formats.Money(confirmed.LedgerFigure), Formats.Money(fresh.LedgerFigure));
        Compare("Saldo resultante", Formats.Money(confirmed.ResultingAmountOwed), Formats.Money(fresh.ResultingAmountOwed));

        CompareLines(lines, "Recibo locatario", confirmed.Locatario, fresh.Locatario);
        CompareLines(lines, "Recibo propietario", confirmed.Propietario, fresh.Propietario);

        return lines;
    }

    private static string PeriodOf(CollectionPreview preview) =>
        preview.Period is { } period ? Formats.Period(period) : None;

    private static void CompareLines(List<DifferenceLine> lines, string copy, ReciboDraft? confirmed, ReciboDraft? fresh)
    {
        var before = confirmed?.Lines ?? [];
        var after = fresh?.Lines ?? [];

        // The confirmed draft's kinds in its order, then any kind only the fresh one has, in its order.
        var kinds = before.Select(l => l.Kind).Concat(after.Select(l => l.Kind)).Distinct();
        foreach (var kind in kinds)
        {
            var was = before.FirstOrDefault(l => l.Kind == kind);
            var now = after.FirstOrDefault(l => l.Kind == kind);
            var wasText = was is null ? None : Formats.Money(was.Amount);
            var nowText = now is null ? None : Formats.Money(now.Amount);

            if (wasText != nowText)
            {
                lines.Add(new DifferenceLine($"{copy} · {ReciboLineLabels.For(kind)}", wasText, nowText));
            }
        }
    }
}
