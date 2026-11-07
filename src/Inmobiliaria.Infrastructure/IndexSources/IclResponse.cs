using System.Globalization;
using System.Text.Json;
using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Infrastructure.IndexSources;

/// <summary>
/// ICL from the BCRA's API, variable 40, on the 1st of the month (spec Decision 41). A month it does not hold is an empty
/// <c>detalle</c>. It answers a date after today with status 400, which is why the screen never asks for a future month.
/// </summary>
internal static class IclResponse
{
    private const string VariableAddress = "monetarias/40";

    public static string Address(string baseAddress, IndexPeriod period)
    {
        var first = FirstOf(period).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{baseAddress}/{VariableAddress}?desde={first}&hasta={first}";
    }

    public static IndexFetch Read(JsonElement root, IndexPeriod period)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0
            || results[0].ValueKind != JsonValueKind.Object
            || !results[0].TryGetProperty("detalle", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return Failed();
        }

        if (rows.GetArrayLength() == 0)
        {
            return new IndexFetch.NotPublishedYet();
        }

        return rows.GetArrayLength() == 1 ? ReadRow(rows[0], period) : Failed();
    }

    private static IndexFetch ReadRow(JsonElement row, IndexPeriod period)
    {
        if (row.ValueKind != JsonValueKind.Object
            || !row.TryGetProperty("fecha", out var date)
            || !row.TryGetProperty("valor", out var value)
            || !SourceValues.TryReadDate(date, out var day)
            || !SourceValues.TryReadLevel(value, out var level)
            || day != FirstOf(period))
        {
            return Failed();
        }

        return new IndexFetch.Found(level);
    }

    private static DateOnly FirstOf(IndexPeriod period) => new(period.Year, period.Month, 1);

    private static IndexFetch.SourceFailed Failed() => new(ServerError: false);
}
