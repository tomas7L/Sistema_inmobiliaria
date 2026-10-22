using System.Text.Json;
using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// The recargo terms as read from configuration: either valid <see cref="RecargoTerms"/> or one problem
/// naming the offending key (design section 10, Decision 33). There is deliberately no fallback value:
/// a missing or out-of-range setting stops the cash desk and the voiding screen instead of silently
/// charging a rate nobody configured. Read from a <see cref="JsonElement"/> so no configuration package
/// is needed and the Linux `core` job can prove it against the shipped file.
/// </summary>
public sealed class RecargoSettings
{
    public const string DailyRateKey = "Recargo:DailyRate";
    public const string GraceDaysKey = "Recargo:GraceDays";

    private RecargoSettings(RecargoTerms? terms, string? problem)
    {
        Terms = terms;
        Problem = problem;
    }

    /// <summary>The valid terms, or null when <see cref="Problem"/> is set.</summary>
    public RecargoTerms? Terms { get; }

    /// <summary>Operator-facing text naming the first invalid key, or null when the terms are valid.</summary>
    public string? Problem { get; }

    public bool IsValid => Terms is not null;

    /// <summary>
    /// Reads the <c>Recargo</c> section of <paramref name="root"/>. The rate is checked first, so a file
    /// with both values wrong reports one problem, the rate; fixing it surfaces the next.
    /// </summary>
    public static RecargoSettings Read(JsonElement root)
    {
        JsonElement section = default;
        var hasSection = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Recargo", out section);

        if (!hasSection || section.ValueKind != JsonValueKind.Object ||
            !section.TryGetProperty("DailyRate", out var rateElement) ||
            rateElement.ValueKind != JsonValueKind.Number ||
            !rateElement.TryGetDecimal(out var dailyRate))
        {
            return Invalid($"La configuración {DailyRateKey} falta o no es un número (debe estar entre 0 y 1).");
        }

        if (!section.TryGetProperty("GraceDays", out var graceElement) ||
            graceElement.ValueKind != JsonValueKind.Number ||
            !graceElement.TryGetInt32(out var graceDays))
        {
            return Invalid($"La configuración {GraceDaysKey} falta o no es un número entero de días (0 o más).");
        }

        // The range checks live in RecargoTerms itself; this only translates their refusal into the key's name.
        try
        {
            return new RecargoSettings(new RecargoTerms(dailyRate, graceDays), problem: null);
        }
        catch (ArgumentException ex) when (ex.ParamName == "dailyRate")
        {
            return Invalid($"La configuración {DailyRateKey} está fuera de rango (debe estar entre 0 y 1).");
        }
        catch (ArgumentException ex) when (ex.ParamName == "graceDays")
        {
            return Invalid($"La configuración {GraceDaysKey} está fuera de rango (debe ser 0 o más).");
        }
    }

    private static RecargoSettings Invalid(string problem) => new(terms: null, problem);
}
