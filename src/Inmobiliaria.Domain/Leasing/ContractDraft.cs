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
public sealed record ClauseDraft(IReadOnlyList<Guid> IndexIds, int IntervalMonths, int WindowOffsetMonths = AdjustmentClause.DefaultWindowOffsetMonths);

/// <summary>The field of the contract form a problem belongs to, in the form's order, so the screen shows it beside that field.</summary>
public enum ContractField
{
    Tenant,
    Lessors,
    Codebtors,
    Units,
    StartDate,
    EndDate,
    Cutover,
    Rent,
    DueDay,
    Honorarios,
    FeePlan,
    Shares,
    Clause,
}

/// <summary>One thing wrong with a draft, and the field it belongs to.</summary>
/// <param name="Row">
/// For a field that is a list (lessors, codeudores, units), the position of the entry it belongs to, so the screen shows it
/// under that row (Mateo's canvas, Tomás 2026-11-16); null when it belongs to the field as a whole.
/// </param>
public sealed record ContractProblem(ContractField Field, ContractRefusal Refusal, int? Row = null);

/// <summary>
/// The form's rules, pure and without a database (design D19). They are separate from <see cref="Contract"/>'s
/// constructor, which keeps tolerating what existing rules and tests use, such as a start date that is not on the 1st
/// (spec Finding F9). Anything needing data is the adapter's.
/// </summary>
public static class ContractDraftRules
{
    /// <summary>The longest a lease may run, for any use (spec Decision 51, Civil and Commercial Code art. 1197).</summary>
    public const int MaximumYears = 50;

    /// <summary>Largest amount <c>numeric(14,2)</c> holds, for the canon and a fee instalment.</summary>
    private const decimal MaximumAmount = 999_999_999_999.99m;

    private const int SharePlaces = 6;
    private const int AmountPlaces = 2;

    /// <summary>The first refusal <paramref name="draft"/> earns, or null when it can be written: the port's guard.</summary>
    /// <param name="draft">The form.</param>
    /// <param name="today">The agency's date.</param>
    /// <param name="standingStart">
    /// On a correction, the start date the contract already has. It is not refused for being in the past or off the 1st: a
    /// contract that has simply waited a month must stay correctable, and an older one was allowed its date when created.
    /// </param>
    public static ContractRefusal? Check(ContractDraft draft, DateOnly today, DateOnly? standingStart = null) =>
        Problems(draft, today, standingStart).FirstOrDefault()?.Refusal;

    /// <summary>
    /// Every problem <paramref name="draft"/> has, in the form's order, each with its field (spec Requirements 27 and 29,
    /// Decision 44): what the form shows at once. Empty when it can be written. Parameters as in <see cref="Check"/>.
    /// </summary>
    public static IReadOnlyList<ContractProblem> Problems(ContractDraft draft, DateOnly today, DateOnly? standingStart = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var problems = new List<ContractProblem>();
        Parties(draft, problems);
        Dates(draft, today, standingStart, problems);
        Money(draft, problems);

        if (draft.Shares.Count > 0 && Shares(draft) is { } shares)
        {
            problems.Add(new ContractProblem(ContractField.Shares, shares));
        }

        if (Clause(draft) is { } clause)
        {
            problems.Add(new ContractProblem(ContractField.Clause, clause));
        }

        return problems;
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

    /// <summary>Whether the tenant is also among the codeudores, which a contract cannot hold (spec Decision 51).</summary>
    public static bool TenantIsCodebtor(Guid? tenantId, IEnumerable<Guid> codebtorIds) =>
        tenantId is { } tenant && codebtorIds.Contains(tenant);

    private static void Parties(ContractDraft draft, List<ContractProblem> problems)
    {
        if (draft.TenantId is null)
        {
            problems.Add(new ContractProblem(ContractField.Tenant, ContractRefusal.TenantMissing));
        }

        if (draft.LessorIds.Count == 0)
        {
            problems.Add(new ContractProblem(ContractField.Lessors, ContractRefusal.LessorMissing));
        }
        else
        {
            Repeats(draft.LessorIds, ContractField.Lessors, ContractRefusal.LessorRepeated, problems);
        }

        for (var row = 0; row < draft.CodebtorIds.Count; row++)
        {
            if (draft.CodebtorIds[row] == draft.TenantId)
            {
                problems.Add(new ContractProblem(ContractField.Codebtors, ContractRefusal.TenantIsCodebtor, row));
            }
        }

        if (draft.Shares.Count == 0)
        {
            problems.Add(new ContractProblem(ContractField.Units, ContractRefusal.UnitMissing));
        }
        else
        {
            Repeats(draft.Shares.Select(s => s.UnitId).ToList(), ContractField.Units, ContractRefusal.UnitRepeated, problems);
        }
    }

    /// <summary>Each entry already listed above it, on its own row; the first of them stays clean (Mateo's canvas).</summary>
    private static void Repeats(IReadOnlyList<Guid> entries, ContractField field, ContractRefusal refusal, List<ContractProblem> problems)
    {
        var seen = new HashSet<Guid>();
        for (var row = 0; row < entries.Count; row++)
        {
            if (!seen.Add(entries[row]))
            {
                problems.Add(new ContractProblem(field, refusal, row));
            }
        }
    }

    private static void Dates(ContractDraft draft, DateOnly today, DateOnly? standingStart, List<ContractProblem> problems)
    {
        if (draft.StartDate is null)
        {
            problems.Add(new ContractProblem(ContractField.StartDate, ContractRefusal.DatesMissing));
        }

        if (draft.NominalEndDate is null)
        {
            problems.Add(new ContractProblem(ContractField.EndDate, ContractRefusal.DatesMissing));
        }

        if (draft is not { StartDate: { } start, NominalEndDate: { } end })
        {
            return;
        }

        if (start.Day != 1 && start != standingStart)
        {
            problems.Add(new ContractProblem(ContractField.StartDate, ContractRefusal.StartNotOnTheFirst));
        }

        if (end <= start)
        {
            problems.Add(new ContractProblem(ContractField.EndDate, ContractRefusal.EndBeforeStart));
        }
        // A start within 50 years of the calendar's last day cannot have an end past the ceiling, and adding would overflow.
        else if (start.Year <= DateOnly.MaxValue.Year - MaximumYears && end > start.AddYears(MaximumYears))
        {
            problems.Add(new ContractProblem(ContractField.EndDate, ContractRefusal.EndPastLegalCeiling));
        }

        if (draft.CutoverDate is { } cutover)
        {
            var refusal = Contract.CheckCutoverBounds(cutover, start, today) switch
            {
                CutoverRefusal.AfterToday => ContractRefusal.CutoverAfterToday,
                CutoverRefusal.BeforeContractStart => ContractRefusal.CutoverBeforeStart,
                _ => (ContractRefusal?)null,
            };
            if (refusal is { } cutoverRefusal)
            {
                problems.Add(new ContractProblem(ContractField.Cutover, cutoverRefusal));
            }

            return;
        }

        if (start < new DateOnly(today.Year, today.Month, 1) && start != standingStart)
        {
            problems.Add(new ContractProblem(ContractField.StartDate, ContractRefusal.StartBeforeCurrentMonth));
        }
    }

    private static void Money(ContractDraft draft, List<ContractProblem> problems)
    {
        if (draft.MonthlyRent is not { } rent || rent <= 0m || rent != decimal.Truncate(rent) || rent > MaximumAmount)
        {
            problems.Add(new ContractProblem(ContractField.Rent, ContractRefusal.RentNotWholePositive));
        }

        if (draft.DueDay is < 1 or > 31)
        {
            problems.Add(new ContractProblem(ContractField.DueDay, ContractRefusal.DueDayOutOfRange));
        }

        if (draft.HonorariosPercentage is { } rate && !IsValidRate(rate))
        {
            problems.Add(new ContractProblem(ContractField.Honorarios, ContractRefusal.HonorariosOutOfRange));
        }

        if (FeePlan(draft) is { } feePlan)
        {
            problems.Add(new ContractProblem(ContractField.FeePlan, feePlan));
        }
    }

    /// <summary>
    /// An amount and 1 to 6 instalments, or neither. The amount is in whole pesos (spec Decision 51, `lease-contract/spec.md:359`),
    /// refused with its own text when it carries cents.
    /// </summary>
    private static ContractRefusal? FeePlan(ContractDraft draft) => (draft.FeePlanAmount, draft.FeePlanInstalments) switch
    {
        (null, null) => null,
        ({ } amount, { } instalments)
            when amount > 0m
                && amount <= MaximumAmount
                && amount == decimal.Round(amount, AmountPlaces)
                && instalments is >= 1 and <= ContractualFeePlan.MaxInstalments =>
            amount == decimal.Truncate(amount) ? null : ContractRefusal.FeePlanNotWholePesos,
        _ => ContractRefusal.FeePlanInvalid,
    };

    private static ContractRefusal? Shares(ContractDraft draft)
    {
        var shares = draft.Shares;
        // A unit twice is refused under the units (UnitRepeated), so only the percentages are checked here.
        var valid = shares.All(s => s.Percentage is > 0m and <= 100m && s.Percentage == decimal.Round(s.Percentage, SharePlaces))
            && shares.Sum(s => s.Percentage) == 100m;

        return valid ? null : ContractRefusal.SharesNotHundred;
    }

    private static ContractRefusal? Clause(ContractDraft draft) => draft.Clause is { } clause ? CheckClause(clause) : null;

    /// <summary>What a clause must hold, on creation and on correction alike.</summary>
    public static ContractRefusal? CheckClause(ClauseDraft clause) =>
        clause.IndexIds.Count == 0
        || clause.IndexIds.Distinct().Count() != clause.IndexIds.Count
        || clause.IntervalMonths is < 1 or > 60
        || !AdjustmentClause.IsValidWindowOffset(clause.WindowOffsetMonths)
            ? ContractRefusal.ClauseInvalid
            : null;
}
