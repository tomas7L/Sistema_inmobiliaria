namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// One confirmed adjustment to a contract's canon — the append-only history entry (spec
/// "Append-Only Adjustment History"). No public mutator and no public setter anywhere on this
/// type; the only way to produce one is <see cref="Confirm"/>, and <see cref="Contract"/> only
/// ever appends, never replaces (design Decision 6).
/// </summary>
public sealed class RentAdjustment
{
    public Guid Id { get; }
    public Guid ContractId { get; }
    public DateOnly EffectiveDate { get; }
    public decimal PreviousCanon { get; }
    public decimal NewCanon { get; }
    public decimal Coefficient { get; }
    public AdjustmentKind Kind { get; }
    public Guid? CorrectsAdjustmentId { get; }
    public DateTimeOffset ConfirmedAt { get; }

    /// <summary>
    /// Who confirmed this adjustment. Nullable here and at the mapped column, because rows
    /// that predate this column stay null forever under the append-only trigger — but
    /// <see cref="Confirm"/>'s <c>confirmedBy</c> parameter is a required, non-nullable
    /// <see cref="Guid"/> (design Decision 8): a NEW row that forgot to name its confirmer is
    /// exactly the defect this asymmetry exists to prevent, so the compiler refuses it at the
    /// one place a null could still be introduced.
    /// </summary>
    public Guid? ConfirmedBy { get; }

    public IReadOnlyList<RentAdjustmentIndexValue> IndexValues { get; }

    /// <summary>
    /// EF Core materialization only. EF writes the mapped properties through their backing
    /// fields and rebuilds <see cref="IndexValues"/> from the persisted rows. It cannot use the
    /// constructor below, because a constructor parameter can never be bound to a navigation.
    /// </summary>
    private RentAdjustment()
    {
        IndexValues = [];
    }

    private RentAdjustment(
        Guid id,
        Guid contractId,
        DateOnly effectiveDate,
        decimal previousCanon,
        decimal newCanon,
        decimal coefficient,
        AdjustmentKind kind,
        Guid? correctsAdjustmentId,
        DateTimeOffset confirmedAt,
        Guid confirmedBy,
        IReadOnlyList<RentAdjustmentIndexValue> indexValues)
    {
        Id = id;
        ContractId = contractId;
        EffectiveDate = effectiveDate;
        PreviousCanon = previousCanon;
        NewCanon = newCanon;
        Coefficient = coefficient;
        Kind = kind;
        CorrectsAdjustmentId = correctsAdjustmentId;
        ConfirmedAt = confirmedAt;
        ConfirmedBy = confirmedBy;
        IndexValues = indexValues;
    }

    /// <summary>
    /// The one factory that produces a confirmed adjustment, and the single
    /// <see cref="decimal.Truncate(decimal)"/> site in the whole pipeline (design Decision 4):
    /// every intermediate value up to <paramref name="proposal"/> stays untruncated `decimal`,
    /// and this is where a whole peso is finally assigned.
    /// </summary>
    public static RentAdjustment Confirm(
        Guid id,
        Guid contractId,
        AdjustmentProposal proposal,
        DateTimeOffset confirmedAt,
        Guid confirmedBy,
        AdjustmentKind kind = AdjustmentKind.Regular,
        Guid? correctsAdjustmentId = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        if ((kind == AdjustmentKind.Correction) != (correctsAdjustmentId is not null))
        {
            throw new ArgumentException(
                "Kind must be Correction if, and only if, CorrectsAdjustmentId is set.",
                nameof(correctsAdjustmentId));
        }

        var newCanon = decimal.Truncate(proposal.UntruncatedNewCanon);

        return new RentAdjustment(
            id,
            contractId,
            proposal.EffectiveDate,
            proposal.PreviousCanon,
            newCanon,
            proposal.Coefficient,
            kind,
            correctsAdjustmentId,
            confirmedAt,
            confirmedBy,
            proposal.IndexValues);
    }
}
