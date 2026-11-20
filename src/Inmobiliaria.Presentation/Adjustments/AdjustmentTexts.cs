using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Adjustments;

/// <summary>
/// What an adjustment's summary and history say, in one place, so the Ajustes screen and the contract detail read the same
/// (task 12.3d: confirming lives in Ajustes, correcting in the detail).
/// </summary>
public static class AdjustmentTexts
{
    /// <summary>The difference offered and the months it covers, or null when none is offered.</summary>
    public static string? Difference(AdjustmentPreview? preview) => preview?.Difference is not { } offer
        ? null
        : offer.Months.Count == 1
        ? $"Diferencia de {Formats.Money(offer.Amount)} por el mes {Formats.Period(offer.Months[0])}, ya cobrado con el canon anterior."
        : $"Diferencia de {Formats.Money(offer.Amount)} por los meses {Formats.Period(offer.Months[0])} a {Formats.Period(offer.Months[^1])}, ya cobrados con el canon anterior.";

    /// <summary>On a correction's summary: which adjustment it corrects, and that it is appended. Null on a regular one.</summary>
    public static string? Correction(AdjustmentPreview? preview) => preview is { CorrectsAdjustmentId: not null } s
        ? $"Corrección del ajuste vencido el {Formats.Date(s.DueDate)}. Se agrega como un ajuste nuevo; el anterior queda como está."
        : null;

    /// <summary>On a correction's summary: what happens to the difference the corrected adjustment charged, or null.</summary>
    public static string? DifferenceOutcome(AdjustmentPreview? preview) => preview switch
    {
        { ReversedDifference: { } reversed } => $"La diferencia de {Formats.Money(reversed)} que cobró el ajuste corregido no se pagó: se revierte al confirmar.",
        { PaidDifferenceStays: { } stays } => $"La diferencia de {Formats.Money(stays)} ya se pagó: queda como está y se resuelve a mano.",
        _ => null,
    };

    /// <summary>Set only when the adjustment prices from a later month than the one it fell due in.</summary>
    public static string? Late(AdjustmentPreview? preview) => preview is { IsLate: true } s
        ? $"Se confirma tarde: el nuevo canon rige desde {Formats.Period(s.AppliesFrom)}. Los meses anteriores quedan con el canon anterior."
        : null;

    /// <summary>After a confirmation: the new canon, the first period it applies to, and the difference charged if any.</summary>
    public static string Confirmed(AdjustmentHistoryEntry confirmed, string opening)
    {
        ArgumentNullException.ThrowIfNull(confirmed);

        var from = Formats.Period(confirmed.AppliesFrom ?? confirmed.DueDate);
        return confirmed is { DifferenceCharged: true, DifferenceAmount: { } charged }
            ? $"{opening}: el canon pasa a {Formats.Money(confirmed.NewCanon)} desde {from}. Se cobra una diferencia de {Formats.Money(charged)} con {from}."
            : $"{opening}: el canon pasa a {Formats.Money(confirmed.NewCanon)} desde {from}.";
    }

    /// <summary>After a confirmation: tell the tenant, since the system sends nothing.</summary>
    public static string Reminder(string? tenantName) => $"Avise a {tenantName ?? "el inquilino"} del nuevo canon. El sistema no le envía nada.";

    /// <summary>One confirmed adjustment as both histories show it.</summary>
    public static AdjustmentHistoryRow HistoryRow(AdjustmentHistoryEntry entry, bool canCorrect)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new AdjustmentHistoryRow(
            Formats.Date(entry.DueDate),
            Formats.Period(entry.AppliesFrom ?? entry.DueDate),
            Formats.Money(entry.PreviousCanon),
            Formats.Money(entry.NewCanon),
            Formats.Level(entry.Coefficient),
            entry.ConfirmedBy ?? "Sin registro",
            entry.DifferenceAmount is { } offered
                ? $"Diferencia de {Formats.Money(offered)} {(entry.DifferenceCharged ? "cobrada" : "no cobrada")}"
                : string.Empty)
        {
            AdjustmentId = entry.Id,
            CanCorrect = canCorrect,
        };
    }

    /// <summary>One index of a summary: its base and end month and level, and its variation.</summary>
    public static AdjustmentIndexRow IndexRow(AdjustmentIndexLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new AdjustmentIndexRow(
            line.IndexName,
            Formats.Period(line.BasePeriod),
            Formats.Level(line.BaseLevel),
            Formats.Period(line.EndPeriod),
            Formats.Level(line.EndLevel),
            Formats.Level(line.Variation))
        {
            VariationPercentText = Percent(line.Variation, signed: true),
        };
    }

    /// <summary>A percentage as the canvas writes it, "5,28 %", or "+5,28 %" when its sign is part of the news.</summary>
    public static string Percent(decimal value, bool signed = false) =>
        $"{(signed && value >= 0m ? "+" : string.Empty)}{value.ToString("0.00", Formats.Agency)} %";

    /// <summary>
    /// How the coefficient came out, worked out (canvas "Confirmar ajuste"): one index's variation, or the average of several
    /// variations, never of their values.
    /// </summary>
    public static string? CoefficientExplanation(AdjustmentPreview? preview)
    {
        if (preview is null || preview.Indices.Count == 0)
        {
            return null;
        }

        if (preview.Indices.Count == 1)
        {
            var line = preview.Indices[0];
            var icl = line.IndexName == "ICL" ? " El ICL es el valor del día 1 del mes." : string.Empty;
            return $"El coeficiente es la variación del {line.IndexName} entre el valor base y el valor final: " +
                $"{Formats.Level(line.EndLevel)} ÷ {Formats.Level(line.BaseLevel)} − 1 = {Percent(preview.Coefficient)}.{icl} " +
                "El cálculo usa todos los decimales.";
        }

        var count = preview.Indices.Count switch { 2 => "dos", 3 => "tres", var n => n.ToString(Formats.Agency) };
        var variations = string.Join(" + ", preview.Indices.Select(i => Percent(i.Variation)));
        return $"El coeficiente es el promedio de las variaciones de los {count} índices, no de sus valores: ({variations}) ÷ " +
            $"{preview.Indices.Count} = {Percent(preview.Coefficient)}. El cálculo usa todos los decimales.";
    }

    /// <summary>How late an adjustment is, by month: "Vence este mes", "Atrasado 1 mes", "Atrasado 2 meses".</summary>
    public static string Late(DateOnly dueDate, DateOnly today)
    {
        var months = ((today.Year * 12) + today.Month) - ((dueDate.Year * 12) + dueDate.Month);
        return months switch
        {
            <= 0 => "Vence este mes",
            1 => "Atrasado 1 mes",
            _ => $"Atrasado {months} meses",
        };
    }

    /// <summary>A clause by its indices: one by its name, several as the average they are.</summary>
    public static string Clause(IReadOnlyList<string> indexNames, CombinationRule combination) =>
        indexNames.Count > 1 || combination == CombinationRule.Average
            ? $"Promedio de {string.Join(" y ", indexNames)}"
            : string.Join(string.Empty, indexNames);

    /// <summary>"Cada 3 meses", or "Cada mes".</summary>
    public static string Every(int intervalMonths) => intervalMonths switch
    {
        <= 0 => string.Empty,
        1 => "Cada mes",
        _ => $"Cada {intervalMonths} meses",
    };
}
