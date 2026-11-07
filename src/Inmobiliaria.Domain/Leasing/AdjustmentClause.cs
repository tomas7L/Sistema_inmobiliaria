namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Records how a specific contract's canon is adjusted: which index or indices, how they
/// combine, at what interval, and by which rounding rule (spec "Per-Contract Adjustment
/// Clause"). A contract with no clause is a valid fixed-price lease (spec "Adjustment Clause
/// Is Optional") — absence of a clause is the absence of a row (design Decision 5); the FK
/// lives here, on <see cref="ContractId"/>, and <see cref="Contract"/> gains no column for it.
/// </summary>
public sealed class AdjustmentClause
{
    /// <summary>Today's window, which a clause recorded before the offset existed keeps (gestion-de-alquileres spec Requirement 13).</summary>
    public const int DefaultWindowOffsetMonths = 1;

    private readonly List<AdjustmentClauseIndex> _indices = [];

    public Guid Id { get; }
    public Guid ContractId { get; }
    public int IntervalMonths { get; private set; }
    public RoundingRule RoundingRule { get; }

    /// <summary>How many months before the due date the index window ends (spec Decision 15). Never changes between adjustments.</summary>
    public int WindowOffsetMonths { get; private set; }

    public IReadOnlyCollection<AdjustmentClauseIndex> Indices => _indices;

    /// <summary>
    /// Derived from the index count, never a stored, independently settable field: `Single`
    /// for exactly one referenced index, `Average` for two or more. The interval is read from
    /// this clause and MUST NOT be assumed six months for any contract (spec).
    /// </summary>
    public CombinationRule Combination =>
        _indices.Count == 1 ? CombinationRule.Single : CombinationRule.Average;

    /// <summary>
    /// EF Core materialization only. EF writes the mapped properties through their backing
    /// fields and rebuilds <see cref="Indices"/> from the persisted rows, so it must not go
    /// through the public constructor — that one takes a list of index ids, which is not a
    /// mapped property and which EF therefore cannot bind.
    /// </summary>
    private AdjustmentClause()
    {
    }

    public AdjustmentClause(
        Guid id,
        Guid contractId,
        IReadOnlyList<Guid> economicIndexIds,
        int intervalMonths,
        RoundingRule roundingRule,
        int windowOffsetMonths = DefaultWindowOffsetMonths)
    {
        ArgumentNullException.ThrowIfNull(economicIndexIds);

        if (economicIndexIds.Count == 0)
        {
            throw new ArgumentException(
                "An adjustment clause must reference at least one index.",
                nameof(economicIndexIds));
        }

        if (economicIndexIds.Distinct().Count() != economicIndexIds.Count)
        {
            throw new ArgumentException(
                "An adjustment clause cannot reference the same index twice.",
                nameof(economicIndexIds));
        }

        Id = id;
        ContractId = contractId;
        RoundingRule = roundingRule;
        Correct(intervalMonths, windowOffsetMonths);

        for (var ordinal = 0; ordinal < economicIndexIds.Count; ordinal++)
        {
            _indices.Add(new AdjustmentClauseIndex(id, economicIndexIds[ordinal], ordinal));
        }
    }

    public static bool IsValidWindowOffset(int windowOffsetMonths) => windowOffsetMonths is >= 0 and <= 3;

    /// <summary>
    /// Corrects the interval and the offset. Whether the clause may still change is the adapter's and the database's: it depends
    /// on the contract's adjustments (spec Decision 32). The indices change through <c>app_replace_clause_indices</c> (design D15).
    /// </summary>
    public void Correct(int intervalMonths, int windowOffsetMonths)
    {
        if (intervalMonths is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalMonths), intervalMonths, "Interval must be between 1 and 60 months.");
        }

        if (!IsValidWindowOffset(windowOffsetMonths))
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowOffsetMonths), windowOffsetMonths, "The window offset must be between 0 and 3 months.");
        }

        IntervalMonths = intervalMonths;
        WindowOffsetMonths = windowOffsetMonths;
    }
}
