namespace Inmobiliaria.Domain.Units;

/// <summary>
/// The 24 jurisdictions of Argentina, the only provinces a unit's address may name (gestion-de-alquileres spec Decision 49):
/// the province is chosen, never typed, so "Sta Fe", "santa fe" and "SF" cannot be stored as three provinces.
/// </summary>
public static class Provinces
{
    /// <summary>The province the form opens on: the agency's own.</summary>
    public const string Default = "Santa Fe";

    /// <summary>In alphabetical order, as the list shows them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "Buenos Aires",
        "Catamarca",
        "Chaco",
        "Chubut",
        "Ciudad Autónoma de Buenos Aires",
        "Córdoba",
        "Corrientes",
        "Entre Ríos",
        "Formosa",
        "Jujuy",
        "La Pampa",
        "La Rioja",
        "Mendoza",
        "Misiones",
        "Neuquén",
        "Río Negro",
        "Salta",
        "San Juan",
        "San Luis",
        "Santa Cruz",
        "Santa Fe",
        "Santiago del Estero",
        "Tierra del Fuego, Antártida e Islas del Atlántico Sur",
        "Tucumán",
    ];

    /// <summary>
    /// Each jurisdiction's letter, the first of its CPA postal codes: its ISO 3166-2:AR code (spec Decision 49).
    /// </summary>
    private static readonly Dictionary<string, char> Letters = new(StringComparer.Ordinal)
    {
        ["Buenos Aires"] = 'B',
        ["Catamarca"] = 'K',
        ["Chaco"] = 'H',
        ["Chubut"] = 'U',
        ["Ciudad Autónoma de Buenos Aires"] = 'C',
        ["Córdoba"] = 'X',
        ["Corrientes"] = 'W',
        ["Entre Ríos"] = 'E',
        ["Formosa"] = 'P',
        ["Jujuy"] = 'Y',
        ["La Pampa"] = 'L',
        ["La Rioja"] = 'F',
        ["Mendoza"] = 'M',
        ["Misiones"] = 'N',
        ["Neuquén"] = 'Q',
        ["Río Negro"] = 'R',
        ["Salta"] = 'A',
        ["San Juan"] = 'J',
        ["San Luis"] = 'D',
        ["Santa Cruz"] = 'Z',
        ["Santa Fe"] = 'S',
        ["Santiago del Estero"] = 'G',
        ["Tierra del Fuego, Antártida e Islas del Atlántico Sur"] = 'V',
        ["Tucumán"] = 'T',
    };

    /// <summary>The letter a CPA of <paramref name="province"/> starts with.</summary>
    public static char CpaLetter(string province) =>
        Letters.TryGetValue(province, out var letter)
            ? letter
            : throw new ArgumentOutOfRangeException(nameof(province), province, "Not one of the 24 jurisdictions.");

    /// <summary>Whether <paramref name="province"/> is one of the list exactly as it is written there.</summary>
    public static bool Contains(string province) => All.Contains(province, StringComparer.Ordinal);
}
