namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// The offset the form suggests from the indices chosen (gestion-de-alquileres spec Decision 15): the last values normally out by
/// the due date. Weak until Nicolás answers N1, and only a suggestion: what the paper says is what the clause records.
/// </summary>
public static class ClauseOffsetSuggestion
{
    /// <summary>The largest any chosen index needs, or null when none of them has a suggestion.</summary>
    public static int? For(IEnumerable<string> indexNames)
    {
        ArgumentNullException.ThrowIfNull(indexNames);

        return indexNames
            .Select(name => name switch
            {
                "ICL" => 0,
                "IPC" => 2,
                "RIPTE" => 3,
                _ => (int?)null,
            })
            .Max();
    }
}
