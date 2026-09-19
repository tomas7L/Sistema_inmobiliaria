namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// The confirm-with-a-summary contract for a rent adjustment (project convention
/// `confirm-with-a-summary`; design Decision 9). <see cref="Contract.ConfirmAdjustment"/> takes
/// this object itself, not loose parameters, so a screen cannot confirm anything other than
/// what it displayed. Carries both the untruncated and the truncated new canon, so the operator
/// sees the peso being dropped rather than discovering it later — this preview computation does
/// not count against "truncation happens exactly once": the canon actually written to the
/// contract is still truncated a single time, inside <see cref="RentAdjustment.Confirm"/>.
/// </summary>
public sealed class AdjustmentProposal
{
    public decimal PreviousCanon { get; }
    public IReadOnlyList<RentAdjustmentIndexValue> IndexValues { get; }
    public CombinationRule Combination { get; }
    public decimal Coefficient { get; }
    public DateOnly EffectiveDate { get; }

    /// <summary>Whole months elapsed since the due date, when confirmation happens late; 0 otherwise.</summary>
    public int MonthsLate { get; }

    /// <summary>Full-precision `decimal` result before the whole-peso rule is applied.</summary>
    public decimal UntruncatedNewCanon { get; }

    /// <summary>Preview of the truncated result, for display only — see remarks above.</summary>
    public decimal TruncatedNewCanon { get; }

    public bool IsLate => MonthsLate > 0;

    public AdjustmentProposal(
        decimal previousCanon,
        IReadOnlyList<RentAdjustmentIndexValue> indexValues,
        CombinationRule combination,
        decimal coefficient,
        DateOnly effectiveDate,
        int monthsLate = 0)
    {
        ArgumentNullException.ThrowIfNull(indexValues);

        if (previousCanon <= 0m)
        {
            throw new ArgumentException("Previous canon must be a positive amount.", nameof(previousCanon));
        }

        if (indexValues.Count == 0)
        {
            throw new ArgumentException("A proposal must carry at least one index value used.", nameof(indexValues));
        }

        if (monthsLate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monthsLate), monthsLate, "Months late cannot be negative.");
        }

        PreviousCanon = previousCanon;
        IndexValues = indexValues;
        Combination = combination;
        Coefficient = coefficient;
        EffectiveDate = effectiveDate;
        MonthsLate = monthsLate;

        UntruncatedNewCanon = previousCanon * (1m + (coefficient / 100m));
        TruncatedNewCanon = decimal.Truncate(UntruncatedNewCanon);
    }
}
