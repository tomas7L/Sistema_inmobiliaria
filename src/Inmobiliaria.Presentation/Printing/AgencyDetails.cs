namespace Inmobiliaria.Presentation.Printing;

/// <summary>
/// The agency's printed letterhead (spec Decision 37, design D15): every item comes from configuration and none
/// from a constant in code. None of it is a secret. Read at print time; a missing item stops the print and is
/// named, and nothing is invented to fill it.
/// </summary>
public sealed record AgencyDetails(
    string Name,
    string Address,
    string Phone,
    string Email,
    string TaxStatus,
    string RentalActivityLegend,
    string BrokerName,
    string BrokerTitle,
    string LicenceNumber,
    string LogoFile)
{
    /// <summary>The configuration section the items live in.</summary>
    public const string Section = "Agency";

    /// <summary>Every item, in the order a problem names them.</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        "Name", "Address", "Phone", "Email", "TaxStatus", "RentalActivityLegend",
        "BrokerName", "BrokerTitle", "LicenceNumber", "LogoFile",
    ];

    /// <summary>The details when every item is present and not blank; otherwise each missing one, named.</summary>
    public static AgencyDetailsRead Read(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        string? Value(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var missing = Keys.Where(k => Value(k) is null).Select(k => $"{Section}:{k}").ToList();
        if (missing.Count > 0)
        {
            return new AgencyDetailsRead(null, missing);
        }

        return new AgencyDetailsRead(
            new AgencyDetails(
                Value("Name")!, Value("Address")!, Value("Phone")!, Value("Email")!, Value("TaxStatus")!,
                Value("RentalActivityLegend")!, Value("BrokerName")!, Value("BrokerTitle")!, Value("LicenceNumber")!,
                Value("LogoFile")!),
            []);
    }
}

/// <summary>What reading the agency section gave: the details, or the items that are missing.</summary>
public sealed record AgencyDetailsRead(AgencyDetails? Details, IReadOnlyList<string> Missing)
{
    /// <summary>The operator text naming every missing item, or null when nothing is missing.</summary>
    public string? Problem => Missing.Count == 0
        ? null
        : "No se puede imprimir: falta configurar " + string.Join(", ", Missing) + " en appsettings.json.";
}
