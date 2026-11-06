namespace Inmobiliaria.Presentation.Indices;

/// <summary>
/// What the form says about each index (gestion-de-alquileres spec Requirement 12): its scale and source, and that the level is
/// the one published for exactly that period (spec Decision 22), since a stand-in would misprice every contract reading it.
/// </summary>
public static class IndexGuidance
{
    private const string ExactPeriod = "Escriba el nivel publicado para exactamente ese período, no el último disponible.";

    public static string For(string indexName) => indexName switch
    {
        "IPC" => $"Número índice, hoy en los miles. Fuente: INDEC. {ExactPeriod}",
        "RIPTE" => $"Monto en pesos, hoy más de un millón. Fuente: Ministerio de Trabajo. {ExactPeriod}",
        "ICL" => $"Índice para contratos de locación, el valor del día 1 del mes. Fuente: BCRA. {ExactPeriod}",
        _ => ExactPeriod,
    };
}
