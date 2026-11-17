using System.Globalization;

namespace Inmobiliaria.Presentation.Common;

/// <summary>What a typed date is for, which decides what a month and year alone mean.</summary>
public enum DateEntryKind
{
    /// <summary>A day: a month and year alone are no date.</summary>
    Day,

    /// <summary>A contract's start: a month and year alone are its first day.</summary>
    Start,

    /// <summary>A contract's end: a month and year alone are its last day.</summary>
    End,
}

/// <summary>
/// A date typed short (Tomás, 2026-11-17: "es molesto tener que escribir la fecha exacta"): day, month and year separated by
/// "/", "-", "." or a space, or run together as ddmmaa or ddmmaaaa; a two-digit year is this century's; and on a start or an
/// end, the month and year alone, also run together as mmaa. Five or seven digits could be two different days (1122026 is
/// 1/12 or 11/2), so they are no date: a contract's date is never guessed.
/// </summary>
public static class DateEntry
{
    private static readonly char[] Separators = ['/', '-', '.', ' '];

    /// <returns>The date, or null when the text is none.</returns>
    public static DateOnly? Parse(string? text, DateEntryKind kind)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }

        var parts = trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts is [var together] && together.All(char.IsAsciiDigit))
        {
            parts = together.Length switch
            {
                6 or 8 => [together[..2], together[2..4], together[4..]],
                4 => [together[..2], together[2..]],
                _ => parts,
            };
        }

        return parts switch
        {
            [var day, var month, var year] => Build(Number(year, isYear: true), Number(month), Number(day)),
            [var month, var year] when kind != DateEntryKind.Day => MonthOnly(Number(year, isYear: true), Number(month), kind),
            _ => null,
        };
    }

    private static int? Number(string text, bool isYear = false)
    {
        if (text.Length is 0 or > 4 || !text.All(char.IsAsciiDigit))
        {
            return null;
        }

        var value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return isYear switch
        {
            true when text.Length == 2 => 2000 + value,
            true when text.Length == 4 => value,
            true => null,
            false when text.Length <= 2 => value,
            _ => null,
        };
    }

    private static DateOnly? Build(int? year, int? month, int? day) =>
        year is { } y && month is >= 1 and <= 12 && day is { } d && d >= 1 && d <= DateTime.DaysInMonth(y, month.Value)
            ? new DateOnly(y, month.Value, d)
            : null;

    private static DateOnly? MonthOnly(int? year, int? month, DateEntryKind kind) =>
        year is { } y && month is >= 1 and <= 12
            ? new DateOnly(y, month.Value, kind == DateEntryKind.Start ? 1 : DateTime.DaysInMonth(y, month.Value))
            : null;
}
