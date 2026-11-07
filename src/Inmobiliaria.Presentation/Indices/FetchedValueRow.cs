using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Indices;

/// <summary>
/// One month the source button asked for (gestion-de-alquileres spec Requirement 25): what the source said, set against what is
/// stored. It offers to store only a month with nothing stored, and a stored level that differs only points to a correction.
/// </summary>
public sealed class FetchedValueRow
{
    public FetchedValueRow(string indexName, IndexPeriod period, IndexFetch fetch, IndexValueListing? stored)
    {
        IndexName = indexName;
        Period = period;
        Fetch = fetch;
        Stored = stored;

        switch (fetch)
        {
            case IndexFetch.Found(var level) when IndexEntryRules.CheckLevel(level) is { } refusal:
                Message = refusal.OperatorText();
                IsFailure = true;
                break;
            case IndexFetch.Found(var level):
                Level = level;
                ReadAgainstStored(level);
                break;
            case IndexFetch.NotPublishedYet:
                Message = $"La fuente todavía no tiene {PeriodText}. Puede que ya esté publicado en el {Publisher(indexName)}: si lo está, escríbalo a mano.";
                break;
            case IndexFetch.Unreachable(TimedOut: true):
                Message = "La fuente tardó demasiado en responder. Pruebe de nuevo o escriba el valor a mano.";
                IsFailure = true;
                break;
            case IndexFetch.Unreachable:
                Message = "No se pudo conectar con la fuente. Revise la conexión a internet o escriba el valor a mano.";
                IsFailure = true;
                break;
            case IndexFetch.SourceFailed(ServerError: true):
                Message = "La fuente respondió con un error. Pruebe más tarde o escriba el valor a mano.";
                IsFailure = true;
                break;
            default:
                Message = "La respuesta de la fuente no se reconoce como válida, así que no se usa. Escriba el valor a mano.";
                IsFailure = true;
                break;
        }
    }

    public string IndexName { get; }

    public IndexPeriod Period { get; }

    public IndexFetch Fetch { get; }

    /// <summary>The value already stored for this month, or null.</summary>
    public IndexValueListing? Stored { get; }

    /// <summary>The level to offer, or null when the source gave none the typed checks would accept.</summary>
    public decimal? Level { get; }

    public string PeriodText => Formats.Period(Period);

    public string? LevelText => Level is { } level ? Formats.Level(level) : null;

    /// <summary>The level stored, shown beside the source's only when the two differ.</summary>
    public string? StoredLevelText => CanCorrect ? Formats.Level(Stored!.Level) : null;

    /// <summary>What to tell the operator about this month, or null when a value is simply waiting to be confirmed.</summary>
    public string? Message { get; private set; }

    /// <summary>True for a failure. A month not published yet, or a stored level that differs, is not one.</summary>
    public bool IsFailure { get; }

    public bool CanStore => Level is not null && Stored is null;

    public bool CanCorrect => Level is { } level && Stored is { } stored && stored.Level != level;

    private void ReadAgainstStored(decimal level)
    {
        if (Stored is not { } stored)
        {
            return;
        }

        Message = stored.Level == level
            ? "Ya está cargado con este valor."
            : $"Ya está cargado con {Formats.Level(stored.Level)}, y la fuente dice {Formats.Level(level)}. No se reemplaza: si hace falta, corríjalo.";
    }

    private static string Publisher(string indexName) => indexName == "IPC" ? "INDEC" : "BCRA";
}
