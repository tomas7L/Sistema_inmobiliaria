using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Aggregate root for a signed lease: lifecycle state and dates, total rent (canon),
/// honorarios rate, party role assignments and their multiplicity, and the per-unit
/// rent split. A contract is always created <see cref="ContractStatus.Active"/> — there
/// is no Draft/pending-signature state.
/// </summary>
public sealed class Contract
{
    private readonly List<ContractParty> _parties = [];
    private readonly List<ContractUnit> _units = [];
    private readonly List<RentAdjustment> _adjustments = [];

    public Guid Id { get; }
    public DateOnly StartDate { get; }
    public DateOnly NominalEndDate { get; }
    public DateOnly? NoticeGivenDate { get; private set; }
    public DateOnly? PlannedMoveOutDate { get; private set; }
    public DateOnly? ActualEndDate { get; private set; }
    public decimal MonthlyRent { get; private set; }
    public decimal? HonorariosPercentage { get; private set; }
    public ContractStatus Status { get; private set; }
    public EndReason? EndReason { get; private set; }

    /// <summary>
    /// A contract MAY have no adjustment clause at all (spec "Adjustment Clause Is Optional").
    /// Absence is the absence of a row, never a null check on a stored column (design Decision
    /// 5): this is a plain EF navigation, not a mapped column.
    /// </summary>
    public AdjustmentClause? AdjustmentClause { get; private set; }

    public IReadOnlyCollection<ContractParty> Parties => _parties;
    public IReadOnlyCollection<ContractUnit> Units => _units;
    public IReadOnlyCollection<RentAdjustment> Adjustments => _adjustments;

    /// <summary>
    /// EF Core materialization only. EF writes the mapped properties through their backing
    /// fields and rebuilds <see cref="Units"/> from the persisted contract_units rows, so it
    /// must not run the public constructor's rent-split validation against an empty list.
    /// </summary>
    private Contract()
    {
    }

    /// <summary>
    /// A contract is valid from birth: it always covers at least one unit, and the split
    /// across those units always sums to exactly 100%. A lease that leases nothing cannot
    /// be constructed. The database trigger is a backstop but cannot catch this case, because
    /// a row-level trigger never fires for a contract with no contract_units rows at all.
    /// </summary>
    public Contract(
        Guid id,
        DateOnly startDate,
        DateOnly nominalEndDate,
        decimal monthlyRent,
        IReadOnlyCollection<UnitShare> unitShares,
        decimal? honorariosPercentage = null)
    {
        if (nominalEndDate < startDate)
        {
            throw new ArgumentException("Nominal end date cannot precede the start date.", nameof(nominalEndDate));
        }

        if (monthlyRent <= 0m)
        {
            throw new ArgumentException("Monthly rent must be a positive amount.", nameof(monthlyRent));
        }

        if (honorariosPercentage is < 0m or > 100m)
        {
            throw new ArgumentException(
                "Honorarios percentage must be between 0 and 100 when provided.",
                nameof(honorariosPercentage));
        }

        ValidateShares(unitShares);

        Id = id;
        StartDate = startDate;
        NominalEndDate = nominalEndDate;
        MonthlyRent = monthlyRent;
        HonorariosPercentage = honorariosPercentage;
        Status = ContractStatus.Active;

        ApplyShares(unitShares);
    }

    /// <summary>
    /// Changes the total canon. Stored per-unit shares are percentages and are left
    /// untouched — they still sum to exactly 100% after the change.
    /// </summary>
    public void ChangeMonthlyRent(decimal monthlyRent)
    {
        if (monthlyRent <= 0m)
        {
            throw new ArgumentException("Monthly rent must be a positive amount.", nameof(monthlyRent));
        }

        MonthlyRent = monthlyRent;
    }

    /// <summary>
    /// Attaches this contract's one <see cref="AdjustmentClause"/>. Rejects a clause recorded
    /// for a different contract; a clause is created once and never reassigned here.
    /// </summary>
    public void AttachAdjustmentClause(AdjustmentClause clause)
    {
        ArgumentNullException.ThrowIfNull(clause);

        if (clause.ContractId != Id)
        {
            throw new ArgumentException("An adjustment clause must belong to this contract.", nameof(clause));
        }

        AdjustmentClause = clause;
    }

    /// <summary>
    /// Confirms a rent adjustment: appends a new <see cref="RentAdjustment"/> to the append-only
    /// history and updates <see cref="MonthlyRent"/> to its already-truncated
    /// <see cref="RentAdjustment.NewCanon"/> via the existing, unchanged
    /// <see cref="ChangeMonthlyRent"/> — truncation itself happens exactly once, inside
    /// <see cref="RentAdjustment.Confirm"/> (design Decision 4), before this method ever sees
    /// the number. Per-unit shares are left untouched by <see cref="ChangeMonthlyRent"/>, so
    /// they still sum to exactly 100% afterward (spec "Share Stored as Percentage, Never
    /// Amount"). No retroactive charge is generated for a late confirmation — this method only
    /// ever changes the canon going forward; billing does not exist in this change.
    /// </summary>
    public RentAdjustment ConfirmAdjustment(
        Guid adjustmentId,
        AdjustmentProposal proposal,
        DateTimeOffset confirmedAt,
        AdjustmentKind kind = AdjustmentKind.Regular,
        Guid? correctsAdjustmentId = null)
    {
        var adjustment = RentAdjustment.Confirm(adjustmentId, Id, proposal, confirmedAt, kind, correctsAdjustmentId);

        ChangeMonthlyRent(adjustment.NewCanon);
        _adjustments.Add(adjustment);

        return adjustment;
    }

    /// <summary>
    /// Assigns <paramref name="party"/> to this contract under <paramref name="role"/>.
    /// Enforces exactly one Tenant and rejects a duplicate Party+Role pair. Lessor and
    /// Codebtor have no upper bound.
    /// </summary>
    public void AssignParty(Party party, PartyRole role)
    {
        ArgumentNullException.ThrowIfNull(party);

        if (_parties.Any(p => p.PartyId == party.Id && p.Role == role))
        {
            throw new InvalidOperationException(
                $"Party '{party.Id}' is already assigned as {role} on this contract.");
        }

        if (role == PartyRole.Tenant && _parties.Any(p => p.Role == PartyRole.Tenant))
        {
            throw new InvalidOperationException("A contract can have exactly one Tenant.");
        }

        _parties.Add(new ContractParty(Id, party.Id, role));
    }

    /// <summary>
    /// Replaces the per-unit rent split. Throws <see cref="RentSplitInvariantException"/>
    /// unless every share is greater than 0% and the shares sum to exactly 100%.
    /// </summary>
    public void SetUnitShares(IReadOnlyCollection<UnitShare> shares)
    {
        ValidateShares(shares);

        _units.Clear();
        ApplyShares(shares);
    }

    /// <summary>
    /// The rent-split invariant, shared by the constructor and <see cref="SetUnitShares"/> so a
    /// contract can never reach an invalid split by either path.
    /// </summary>
    private static void ValidateShares(IReadOnlyCollection<UnitShare> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);

        if (shares.Count == 0)
        {
            throw new RentSplitInvariantException("A contract must cover at least one unit.");
        }

        // Checked before the sum, because two 50% rows for the same unit add up to 100% and
        // would otherwise pass. The composite primary key on contract_units rejects this too,
        // but only at save time and with an opaque database error; the caller deserves to be
        // told which rule it broke while it can still fix the input.
        if (shares.Select(s => s.UnitId).Distinct().Count() != shares.Count)
        {
            throw new RentSplitInvariantException("A unit may appear only once in a contract's rent split.");
        }

        if (shares.Any(s => s.Percentage <= 0m))
        {
            throw new RentSplitInvariantException("Every unit share must be greater than 0%.");
        }

        if (shares.Sum(s => s.Percentage) != 100m)
        {
            throw new RentSplitInvariantException("Unit shares must sum to exactly 100%.");
        }
    }

    private void ApplyShares(IReadOnlyCollection<UnitShare> shares)
    {
        foreach (var share in shares)
        {
            _units.Add(new ContractUnit(Id, share.UnitId, share.Percentage));
        }
    }

    /// <summary>
    /// Records notice of termination, moving the contract from <see cref="ContractStatus.Active"/>
    /// to <see cref="ContractStatus.PendingTermination"/>. A contract past its
    /// <see cref="NominalEndDate"/> stays <see cref="ContractStatus.Active"/> until this is called
    /// (no tácita reconducción / automatic renewal transition).
    /// </summary>
    public void GiveNotice(DateOnly noticeGivenDate, DateOnly plannedMoveOutDate)
    {
        if (Status != ContractStatus.Active)
        {
            throw new InvalidOperationException(
                $"Notice can only be given from the Active state; current state is {Status}.");
        }

        NoticeGivenDate = noticeGivenDate;
        PlannedMoveOutDate = plannedMoveOutDate;
        Status = ContractStatus.PendingTermination;
    }

    /// <summary>
    /// Ends the contract. A reason is mandatory — ending without one is rejected.
    /// </summary>
    public void End(EndReason? reason, DateOnly actualEndDate)
    {
        if (reason is null)
        {
            throw new InvalidOperationException("An end reason is required to end a contract.");
        }

        if (Status == ContractStatus.Ended)
        {
            throw new InvalidOperationException("Contract is already Ended.");
        }

        ActualEndDate = actualEndDate;
        EndReason = reason;
        Status = ContractStatus.Ended;
    }
}
