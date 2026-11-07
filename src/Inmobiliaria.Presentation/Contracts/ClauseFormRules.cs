using System.Globalization;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>
/// What a form does with a clause's typed fields (gestion-de-alquileres spec Requirement 13, Decision 15), on creation and on
/// correction alike. An offset left blank takes the suggestion or today's window, and the months shown say which it was.
/// </summary>
public static class ClauseFormRules
{
    /// <returns>False when a typed number is not one. Whether its value is in range is the rules' to say.</returns>
    public static bool TryBuild(
        IReadOnlyCollection<Guid> indexIds, string intervalText, string offsetText, int? suggestedOffset, out ClauseDraft? clause)
    {
        clause = null;
        if (indexIds.Count == 0 && string.IsNullOrWhiteSpace(intervalText) && string.IsNullOrWhiteSpace(offsetText))
        {
            return true;
        }

        if (!TryReadNumber(intervalText, out var interval) || !TryReadOffset(offsetText, suggestedOffset, out var offset))
        {
            return false;
        }

        clause = new ClauseDraft(indexIds.ToList(), interval, offset);
        return true;
    }

    /// <summary>The due date and the months it will read, or null while the fields do not name them yet.</summary>
    public static string? WindowText(
        DateOnly? startDate, DateOnly? cutoverDate, string intervalText, string offsetText, int? suggestedOffset, DateOnly? lastEffectiveDate = null)
    {
        if (startDate is not { } start
            || !TryReadNumber(intervalText, out var interval)
            || interval is < 1 or > 60
            || !TryReadOffset(offsetText, suggestedOffset, out var offset)
            || !AdjustmentClause.IsValidWindowOffset(offset))
        {
            return null;
        }

        var due = AdjustmentSchedule.NextDueDate(start, interval, lastEffectiveDate, cutoverDate);
        var window = AdjustmentWindow.For(due, interval, offset);

        return $"El próximo ajuste vence el {Formats.Date(due)} y lee de {Formats.Period(window.Base)} a {Formats.Period(window.End)}.";
    }

    private static bool TryReadOffset(string text, int? suggested, out int offset)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            offset = suggested ?? AdjustmentClause.DefaultWindowOffsetMonths;
            return true;
        }

        return TryReadNumber(text, out offset);
    }

    private static bool TryReadNumber(string text, out int number) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
}
