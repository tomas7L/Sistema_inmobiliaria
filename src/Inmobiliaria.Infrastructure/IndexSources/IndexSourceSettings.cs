using System.Text.Json;

namespace Inmobiliaria.Infrastructure.IndexSources;

/// <summary>
/// The sources' addresses as read from configuration (gestion-de-alquileres design D22, §5): both valid, or one problem naming
/// the offending key and no address. Read from a <see cref="JsonElement"/> like <see cref="Access.RecargoSettings"/>. Unlike
/// recargo, a problem here closes nothing but the button, which then reads as a failing source and leaves manual entry open.
/// </summary>
public sealed class IndexSourceSettings
{
    public const string BcraKey = "IndexSources:Bcra";
    public const string DatosGobKey = "IndexSources:DatosGob";

    private IndexSourceSettings(string? bcra, string? datosGob, string? problem)
    {
        Bcra = bcra;
        DatosGob = datosGob;
        Problem = problem;
    }

    /// <summary>The BCRA API's base address with no trailing slash, or null when <see cref="Problem"/> is set.</summary>
    public string? Bcra { get; }

    /// <summary>The series API's base address with no trailing slash, or null when <see cref="Problem"/> is set.</summary>
    public string? DatosGob { get; }

    /// <summary>Text naming the first invalid key, or null when both addresses are valid.</summary>
    public string? Problem { get; }

    public static IndexSourceSettings Read(JsonElement root)
    {
        JsonElement section = default;
        var hasSection = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("IndexSources", out section)
            && section.ValueKind == JsonValueKind.Object;

        if (!hasSection || !TryReadAddress(section, "Bcra", out var bcra))
        {
            return Invalid(BcraKey);
        }

        return TryReadAddress(section, "DatosGob", out var datosGob)
            ? new IndexSourceSettings(bcra, datosGob, problem: null)
            : Invalid(DatosGobKey);
    }

    private static bool TryReadAddress(JsonElement section, string name, out string address)
    {
        address = string.Empty;
        if (!section.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(element.GetString(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        address = element.GetString()!.TrimEnd('/');
        return true;
    }

    private static IndexSourceSettings Invalid(string key) =>
        new(bcra: null, datosGob: null, $"La configuración {key} falta o no es una dirección https.");
}
