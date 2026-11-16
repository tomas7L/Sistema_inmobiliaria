using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Presentation.Indices;

/// <summary>
/// What the form says about each index (gestion-de-alquileres spec Requirement 12): its scale and source, and that the level is
/// the one published for exactly that period (spec Decision 22), since a stand-in would misprice every contract reading it.
/// </summary>
public static class IndexGuidance
{
    private const string ExactPeriod = "Escriba el nivel publicado para exactamente ese período, no el último disponible.";

    private const string FarLevel =
        "Si el nivel se aleja más de un 25 % por cada mes de distancia del valor anterior, el sistema pide confirmar de nuevo.";

    public static string For(string indexName) => indexName switch
    {
        "IPC" => $"Número índice, hoy en los miles. Fuente: INDEC. {ExactPeriod}",
        "RIPTE" => $"Monto en pesos, hoy más de un millón. Fuente: Ministerio de Trabajo. {ExactPeriod}",
        "ICL" => $"Índice para contratos de locación, el valor del día 1 del mes. Fuente: BCRA. {ExactPeriod}",
        _ => ExactPeriod,
    };

    /// <summary>Who publishes the index, as the list names it under the index.</summary>
    public static string Source(string indexName) => indexName switch
    {
        "IPC" => "INDEC",
        "RIPTE" => "Ministerio de Trabajo",
        "ICL" => "BCRA",
        _ => string.Empty,
    };

    /// <summary>"$" for RIPTE, an amount in pesos; empty for an index number.</summary>
    public static string Unit(string indexName) => indexName == "RIPTE" ? "$" : string.Empty;

    /// <summary>The note under the form, with the months the index takes (spec Decision 53).</summary>
    public static string FormHint(string indexName)
    {
        var months = IndexEntryRules.IsPublishedDaily(indexName)
            ? "Se puede cargar cualquier mes desde 2000, salteado o viejo, menos uno futuro; el mes en curso sí."
            : "Se puede cargar cualquier mes desde 2000, salteado o viejo, menos el mes en curso y los futuros, que todavía no se publicaron.";

        return $"Es el nivel publicado para exactamente ese período, no el último disponible. Puede tener hasta 6 decimales. {months} {FarLevel}";
    }
}
