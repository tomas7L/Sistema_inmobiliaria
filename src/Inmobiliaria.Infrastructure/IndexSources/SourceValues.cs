using System.Globalization;
using System.Text.Json;

namespace Inmobiliaria.Infrastructure.IndexSources;

/// <summary>What both sources' answers must hold: an ISO date, and a positive level read as a decimal, never floating point.</summary>
internal static class SourceValues
{
    public static bool TryReadDate(JsonElement element, out DateOnly date)
    {
        date = default;
        return element.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static bool TryReadLevel(JsonElement element, out decimal level)
    {
        level = 0m;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out level) && level > 0m;
    }
}
