using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>A contract as the operator filled the form, on the way to being created (gestion-de-alquileres spec Requirements 7 and 8).</summary>
public sealed record ContractDraft
{
    public Guid? TenantId { get; init; }

    public IReadOnlyList<Guid> LessorIds { get; init; } = [];

    public IReadOnlyList<Guid> CodebtorIds { get; init; } = [];

    public IReadOnlyList<UnitShare> Shares { get; init; } = [];

    public DateOnly? StartDate { get; init; }

    public DateOnly? NominalEndDate { get; init; }

    public decimal? MonthlyRent { get; init; }

    public int DueDay { get; init; } = Contract.DefaultDueDay;

    /// <summary>Null is "not entered", and the desk refuses collection until it is; 0% is an entered rate.</summary>
    public decimal? HonorariosPercentage { get; init; }

    /// <summary>Only an Admin's form offers it, and the database refuses anyone else's on insert.</summary>
    public DateOnly? CutoverDate { get; init; }

    public decimal? FeePlanAmount { get; init; }

    public int? FeePlanInstalments { get; init; }

    public ClauseDraft? Clause { get; init; }
}

/// <param name="IndexIds">The clause's indices, in order.</param>
/// <param name="IntervalMonths">Months between adjustments.</param>
public sealed record ClauseDraft(IReadOnlyList<Guid> IndexIds, int IntervalMonths);

/// <summary>
/// The form's rules, pure and without a database (design D19). They are separate from <see cref="Contract"/>'s
/// constructor, which keeps tolerating what existing rules and tests use, such as a start date that is not on the 1st
/// (spec Finding F9). Anything needing data is the adapter's.
/// </summary>
public static class ContractDraftRules
{
    /// <summary>Largest amount <c>numeric(14,2)</c> holds, for the canon and a fee instalment.</summary>
    private const decimal MaximumAmount = 999_999_999_999.99m;

    private const int SharePlaces = 6;
    private const int AmountPlaces = 2;

    /// <summary>The first refusal <paramref name="draft"/> earns, or null when it can be written.</summary>
    /// <param name="draft">The form.</param>
    /// <param name="today">The agency's date.</param>
    /// <param name="standingStart">
    /// On a correction, the start date the contract already has. It is not refused for being in the past or off the 1st: a
    /// contract that has simply waited a month must stay correctable, and an older one was allowed its date when created.
    /// </param>
    public static ContractRefusal? Check(ContractDraft draft, DateOnly today, DateOnly? standingStart = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return Parties(draft) ?? Dates(draft, today, standingStart) ?? Money(draft) ?? Shares(draft) ?? Clause(draft);
    }

    /// <summary>A rate as the form takes it: 0 to 100, with up to two decimals.</summary>
    public static bool IsValidRate(decimal rate) => rate is >= 0m and <= 100m && rate == decimal.Round(rate, AmountPlaces);

    /// <summary>What the operator should see before going ahead, as far as the form alone can tell.</summary>
    public static IReadOnlyList<ContractWarning> Warnings(ContractDraft draft, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var warnings = new List<ContractWarning>();
        if (draft.HonorariosPercentage is null)
        {
            warnings.Add(new ContractWarning(ContractWarningKind.NoHonorarios));
        }

        // A cutover carries the months on or before it, so nothing is overdue because of the start date.
        if (draft is { StartDate: { } start, CutoverDate: null }
            && (start.Year, start.Month) == (today.Year, today.Month)
            && draft.DueDay is >= 1 and <= 31
            && AccrualSchedule.DueDateFor(start, draft.DueDay) < today)
        {
            warnings.Add(new ContractWarning(ContractWarningKind.FirstDueDatePassed, Date: start));
        }

        return warnings;
    }

    private static ContractRefusal? Parties(ContractDraft draft)
    {
        if (draft.TenantId is null)
        {
            return ContractRefusal.TenantMissing;
        }

        if (draft.LessorIds.Count == 0)
        {
            return ContractRefusal.LessorMissing;
        }

        return draft.Shares.Count == 0 ? ContractRefusal.UnitMissing : null;
    }

    private static ContractRefusal? Dates(ContractDraft draft, DateOnly today, DateOnly? standingStart)
    {
        if (draft is not { StartDate: { } start, NominalEndDate: { } end })
        {
            return ContractRefusal.DatesMissing;
        }

        if (start.Day != 1 && start != standingStart)
        {
            return ContractRefusal.StartNotOnTheFirst;
        }

        if (end < start)
        {
            return ContractRefusal.EndBeforeStart;
        }

        if (draft.CutoverDate is { } cutover)
        {
            return Contract.CheckCutoverBounds(cutover, start, today) switch
            {
                CutoverRefusal.AfterToday => ContractRefusal.CutoverAfterToday,
                CutoverRefusal.BeforeContractStart => ContractRefusal.CutoverBeforeStart,
                _ => null,
            };
        }

        return start < new DateOnly(today.Year, today.Month, 1) && start != standingStart ? ContractRefusal.StartBeforeCurrentMonth : null;
    }

    private static ContractRefusal? Money(ContractDraft draft)
    {
        if (draft.MonthlyRent is not { } rent || rent <= 0m || rent != decimal.Truncate(rent) || rent > MaximumAmount)
        {
            return ContractRefusal.RentNotWholePositive;
        }

        if (draft.DueDay is < 1 or > 31)
        {
            return ContractRefusal.DueDayOutOfRange;
        }

        if (draft.HonorariosPercentage is { } rate && !IsValidRate(rate))
        {
            return ContractRefusal.HonorariosOutOfRange;
        }

        return FeePlan(draft);
    }

    private static ContractRefusal? FeePlan(ContractDraft draft) => (draft.FeePlanAmount, draft.FeePlanInstalments) switch
    {
        (null, null) => null,
        ({ } amount, { } instalments)
            when amount > 0m
                && amount <= MaximumAmount
                && amount == decimal.Round(amount, AmountPlaces)
                && instalments is >= 1 and <= ContractualFeePlan.MaxInstalments => null,
        _ => ContractRefusal.FeePlanInvalid,
    };

    private static ContractRefusal? Shares(ContractDraft draft)
    {
        var shares = draft.Shares;
        var valid = shares.Select(s => s.UnitId).Distinct().Count() == shares.Count
            && shares.All(s => s.Percentage is > 0m and <= 100m && s.Percentage == decimal.Round(s.Percentage, SharePlaces))
            && shares.Sum(s => s.Percentage) == 100m;

        return valid ? null : ContractRefusal.SharesNotHundred;
    }

    private static ContractRefusal? Clause(ContractDraft draft) =>
        draft.Clause is { } clause
        && (clause.IndexIds.Count == 0
            || clause.IndexIds.Distinct().Count() != clause.IndexIds.Count
            || clause.IntervalMonths is < 1 or > 60)
            ? ContractRefusal.ClauseInvalid
            : null;
}
