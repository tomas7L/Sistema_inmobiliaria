using System.Globalization;
using System.Text;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The account search's text rule (ui-de-cobranza design D17), done in memory over a few hundred contracts
/// at most, so no database extension is needed: the query and each candidate field are folded (lower case,
/// accents removed, surrounding space trimmed) and matched by substring. Pure, so it is tested without a
/// database.
/// </summary>
public static class AccountSearch
{
    /// <summary>Fewer meaningful characters than this find nothing.</summary>
    public const int MinimumLength = 2;

    /// <summary>The most results a search returns.</summary>
    public const int MaxResults = 50;

    /// <summary>"Demaría" and "demaria" fold to the same text.</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Whether <paramref name="text"/> is long enough to search for.</summary>
    public static bool IsSearchable(string? text) => text is not null && Fold(text).Length >= MinimumLength;

    /// <summary>
    /// The candidates any of whose fields contains <paramref name="text"/>, sorted by the folded
    /// <paramref name="sortKey"/> (ordinal, so the order does not depend on the machine's culture), at most
    /// <see cref="MaxResults"/>. <paramref name="tieBreak"/>, when given, orders candidates whose sort keys
    /// fold equal, so two tenants with the same name always come back in the same order.
    /// </summary>
    public static IReadOnlyList<T> Select<T>(
        IEnumerable<T> candidates,
        string text,
        Func<T, IEnumerable<string>> fields,
        Func<T, string> sortKey,
        Func<T, string>? tieBreak = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(sortKey);

        if (!IsSearchable(text))
        {
            return [];
        }

        var query = Fold(text);

        return candidates
            .Where(c => fields(c).Any(field => Fold(field).Contains(query, StringComparison.Ordinal)))
            .OrderBy(c => Fold(sortKey(c)), StringComparer.Ordinal)
            .ThenBy(c => tieBreak?.Invoke(c) ?? string.Empty, StringComparer.Ordinal)
            .Take(MaxResults)
            .ToList();
    }
}
