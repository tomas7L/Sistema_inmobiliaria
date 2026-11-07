using System.Globalization;
using System.Text.Json;
using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Infrastructure.IndexSources;

/// <summary>
/// National IPC from the series API. <c>start_date</c> brings the first row from that month on, so a month it does not hold is
/// either no row or a later one, and an earlier one is a source that stopped behaving as it should.
/// </summary>
internal static class IpcResponse
{
    private const string Series = "148.3_INIVELNAL_DICI_M_26";

    public static string Address(string baseAddress, IndexPeriod period)
    {
        var first = new DateOnly(period.Year, period.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{baseAddress}/series/?ids={Series}&start_date={first}&limit=1&format=json";
    }

    public static IndexFetch Read(JsonElement root, IndexPeriod period)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return Failed();
        }

        return rows.GetArrayLength() == 0 ? new IndexFetch.NotPublishedYet() : ReadRow(rows[0], period);
    }

    private static IndexFetch ReadRow(JsonElement row, IndexPeriod period)
    {
        if (row.ValueKind != JsonValueKind.Array
            || row.GetArrayLength() != 2
            || !SourceValues.TryReadDate(row[0], out var date))
        {
            return Failed();
        }

        var month = new IndexPeriod(date.Year, date.Month);
        if (month > period)
        {
            return new IndexFetch.NotPublishedYet();
        }

        return month == period && SourceValues.TryReadLevel(row[1], out var level) ? new IndexFetch.Found(level) : Failed();
    }

    private static IndexFetch.SourceFailed Failed() => new(ServerError: false);
}
