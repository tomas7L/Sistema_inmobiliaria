using System.Globalization;
using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Notices;

/// <summary>
/// A notice's email, in plain text (notificaciones design D10, spec Requirement 2). It states the date its figures are for and
/// that recargo grows after it, names what the figures cover in the Mora screen's words, and names the recargo inside the
/// total, never beside it. The Spanish wording is a placeholder until Tomás reads one real notice (tasks 4.2).
/// </summary>
public static class NoticeComposer
{
    private static readonly CultureInfo Agency = CultureInfo.GetCultureInfo("es-AR");

    public static OutgoingMail Compose(TenantNotice notice, string agencyName)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentException.ThrowIfNullOrWhiteSpace(agencyName);

        var day = LongDate(notice.FiguresOn);
        var subject = $"{agencyName}: estado de su cuenta al {day}";

        var recargo = notice.Recargo > 0m
            ? $" Ese total ya incluye {Money(notice.Recargo)} de recargo por mora"
              + (notice.DaysOverdue > 0 ? $"; la deuda más antigua lleva {notice.DaysOverdue} días de atraso." : ".")
            : string.Empty;

        var body = string.Join(
            "\n\n",
            $"Hola, {notice.Tenant}:",
            $"Le escribimos de {agencyName} por el contrato {notice.ContractShortId} ({notice.Units}).",
            $"Al {day}, el total adeudado es de {Money(notice.Total)}.{recargo}",
            "Después de esa fecha el importe puede aumentar, porque el recargo por mora crece cada día de atraso.",
            BalanceCoverage.Sentence,
            "Si ya realizó el pago, por favor no tenga en cuenta este aviso.",
            $"Saludos,\n{agencyName}");

        return new OutgoingMail(notice.Recipient, subject, body + "\n");
    }

    /// <summary>"3 de diciembre de 2026".</summary>
    private static string LongDate(DateOnly date) => date.ToString("d 'de' MMMM 'de' yyyy", Agency);

    /// <summary>"$ 1.054.800", with up to two decimals when there are cents, as every screen writes money.</summary>
    private static string Money(decimal amount) => "$ " + amount.ToString("#,##0.##", Agency);
}
