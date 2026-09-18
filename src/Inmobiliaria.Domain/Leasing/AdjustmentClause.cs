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
    private readonly List<AdjustmentClauseIndex> _indices = [];

    public Guid Id { get; }
    public Guid ContractId { get; }
    public int IntervalMonths { get; }
    public RoundingRule RoundingRule { get; }

    public IReadOnlyCollection<AdjustmentClauseIndex> Indices => _indices;

    /// <summary>
    /// Derived from the index count, never a stored, independently settable field: `Single`
    /// for exactly one referenced index, `Average` for two or more. The interval is read from
    /// this clause and MUST NOT be assumed six months for any contract (spec).
    /// </summary>
    public CombinationRule Combination =>
        _indices.Count == 1 ? CombinationRule.Single : CombinationRule.Average;

    public AdjustmentClause(
        Guid id,
        Guid contractId,
        IReadOnlyList<Guid> economicIndexIds,
        int intervalMonths,
        RoundingRule roundingRule)
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

        if (intervalMonths is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalMonths), intervalMonths, "Interval must be between 1 and 60 months.");
        }

        Id = id;
        ContractId = contractId;
        IntervalMonths = intervalMonths;
        RoundingRule = roundingRule;

        for (var ordinal = 0; ordinal < economicIndexIds.Count; ordinal++)
        {
            _indices.Add(new AdjustmentClauseIndex(id, economicIndexIds[ordinal], ordinal));
        }
    }
}
