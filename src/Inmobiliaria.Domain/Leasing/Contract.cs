using Inmobiliaria.Domain.Accounts;
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

    // The three go-live columns, held in fields so a method can set them while the public members
    // stay get-only (and out of the EF model until ContractConfiguration names these fields).
    // smallint on the database side, hence short.
    private DateOnly? _cutoverDate;
    private decimal? _contractualFeeAmount;
    private short? _contractualFeeInstalments;

    public Guid Id { get; }
    public DateOnly StartDate { get; }
    public DateOnly NominalEndDate { get; }
    public DateOnly? NoticeGivenDate { get; private set; }
    public DateOnly? PlannedMoveOutDate { get; private set; }
    public DateOnly? ActualEndDate { get; private set; }
    public decimal MonthlyRent { get; private set; }
    public decimal? HonorariosPercentage { get; private set; }

    /// <summary>
    /// Day of the month this contract's rent falls due, and the day mora starts counting from.
    /// Ten by default, which is what the agency's leases say today and what every existing row
    /// will carry after the migration.
    ///
    /// This field did not exist until now, and three documents of the cuenta-corriente change
    /// claimed it did (design Decision 9). Every mora figure in the system is measured from it,
    /// so a hard-coded 10 would have put a business term the parties sign into the source.
    ///
    /// Deliberately get-only. A settable property is mapped by EF convention the moment it is
    /// added, which puts the model out of step with the last migration and makes every
    /// Testcontainers test fail before it runs its first assertion. A get-only property enters
    /// the model only when a configuration names it — the way <see cref="StartDate"/> does —
    /// which is what lets the column and its migration ship together in one change.
    /// </summary>
    public int DueDay { get; } = DefaultDueDay;

    /// <summary>
    /// The agency's standard due day. A default, never a rule: the owner can set another one per
    /// contract, because the system should not refuse a lease that happens to say the 5th.
    /// </summary>
    public const int DefaultDueDay = 10;

    /// <summary>
    /// The go-live date of a contract that predates the system, and null for one created here. The
    /// accrual schedule produces no period whose due date falls on or before it, rent and
    /// instalments alike: those months are carried by the opening balance, month by month, or are
    /// not owed (cobranza-y-recibos design D17).
    ///
    /// It is stored on the contract rather than derived from the earliest opening balance, because
    /// an account read before its opening balances are typed would have no cutover and would
    /// materialise its whole history onto an append-only ledger, permanently.
    ///
    /// Deliberately get-only, like <see cref="DueDay"/>: EF leaves it out of the model until a
    /// configuration names it, which lets the column and its migration ship together in the change
    /// that adds them.
    ///
    /// Backed by a private field because a get-only property cannot be assigned by a method, and
    /// the field is what EF writes when it loads the row (named in ContractConfiguration). It is
    /// set at construction or by <see cref="SetCutoverDate"/>, never by anything else.
    /// </summary>
    public DateOnly? CutoverDate => _cutoverDate;

    /// <summary>
    /// The honorarios contractuales plan, or null when the contract has none (the default). Fixed
    /// once the account holds any movement: <see cref="ChangeContractualFeePlan"/> refuses then.
    /// The amount never moves when the rent adjusts.
    ///
    /// Not mapped as a property: a plan is two columns, so the model names the two private fields
    /// behind it instead (ContractConfiguration), which keeps this type out of the EF model.
    /// </summary>
    public ContractualFeePlan? ContractualFeePlan =>
        _contractualFeeAmount is { } amount && _contractualFeeInstalments is { } instalments
            ? new ContractualFeePlan(amount, instalments)
            : null;

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
        decimal? honorariosPercentage = null,
        int dueDay = DefaultDueDay,
        DateOnly? cutoverDate = null,
        ContractualFeePlan? contractualFeePlan = null)
    {
        // Any calendar day is allowed, including the 29th through the 31st. A month that is too
        // short clamps to its own last day when the due date is computed, rather than being
        // refused here: a lease that genuinely says "the 31st" is a lease the system must accept.
        if (dueDay is < 1 or > 31)
        {
            throw new ArgumentException(
                "A due day must be a day of the month, between 1 and 31.", nameof(dueDay));
        }

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
        DueDay = dueDay;
        _cutoverDate = cutoverDate;
        StoreFeePlan(contractualFeePlan);
        Status = ContractStatus.Active;

        ApplyShares(unitShares);
    }

    /// <summary>
    /// Sets or clears the honorarios contractuales plan. Refused once the contract's account holds
    /// ANY movement, the same rule as <see cref="SetCutoverDate"/>. Stricter than spec Decision 32
    /// ("not after any instalment"), per review finding R3-fee-plan-after-rent-silently-dropped: the
    /// materialiser skips an already-charged period as a whole, so a plan accepted after rent was
    /// written would never raise its instalments, and backfilling them would turn paid months back
    /// into owed ones. The account is passed in because the contract does not hold it; the database
    /// trigger says the same thing against raw SQL.
    /// </summary>
    public void ChangeContractualFeePlan(ContractualFeePlan? plan, ContractAccount account)
    {
        RequireOwnAccount(account);

        if (account.Movements.Count > 0)
        {
            throw new InvalidOperationException(
                "The fee plan cannot change once the account holds a movement.");
        }

        StoreFeePlan(plan);
    }

    /// <summary>
    /// Records that this contract predates the system and goes live on <paramref name="cutoverDate"/>
    /// (design D17): the schedule then produces nothing due on or before it. One-way once the
    /// account holds any movement, because by then the account has already been read and either
    /// carries the history or has been told it does not. The database trigger repeats this rule
    /// (and, since ui-de-cobranza, refuses the change to anyone but an Admin).
    /// The date must lie between <see cref="StartDate"/> and <paramref name="today"/>, both included
    /// (ui-de-cobranza design D5); <paramref name="today"/> is an argument because Domain has no clock.
    /// Call <see cref="CheckCutoverDate"/> first to get the refusal as a value.
    /// </summary>
    public void SetCutoverDate(DateOnly cutoverDate, ContractAccount account, DateOnly today)
    {
        switch (CheckCutoverDate(cutoverDate, account, today))
        {
            case CutoverRefusal.AccountHasMovement:
                throw new InvalidOperationException(
                    "The cutover date cannot be set once the account holds a movement.");
            case CutoverRefusal.AfterToday:
                throw new ArgumentOutOfRangeException(
                    nameof(cutoverDate), cutoverDate, "The cutover date cannot be after today.");
            case CutoverRefusal.BeforeContractStart:
                throw new ArgumentOutOfRangeException(
                    nameof(cutoverDate), cutoverDate, "The cutover date cannot be before the contract's start date.");
        }

        _cutoverDate = cutoverDate;
    }

    /// <summary>
    /// The rule of <see cref="SetCutoverDate"/> as a value, null when the date would be accepted. Checked
    /// in this order: the account is this contract's own (still an <see cref="ArgumentException"/>, a
    /// programming error rather than a refusal), the account holds no movement, the date is not after
    /// <paramref name="today"/>, the date is not before <see cref="StartDate"/>.
    /// </summary>
    public CutoverRefusal? CheckCutoverDate(DateOnly cutoverDate, ContractAccount account, DateOnly today)
    {
        RequireOwnAccount(account);

        if (account.Movements.Count > 0)
        {
            return CutoverRefusal.AccountHasMovement;
        }

        if (cutoverDate > today)
        {
            return CutoverRefusal.AfterToday;
        }

        if (cutoverDate < StartDate)
        {
            return CutoverRefusal.BeforeContractStart;
        }

        return null;
    }

    private void RequireOwnAccount(ContractAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.ContractId != Id)
        {
            throw new ArgumentException("The account must belong to this contract.", nameof(account));
        }
    }

    private void StoreFeePlan(ContractualFeePlan? plan)
    {
        _contractualFeeAmount = plan?.Amount;
        _contractualFeeInstalments = plan is null ? null : (short)plan.Instalments;
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

        // The documentation above claimed a clause is never reassigned, and nothing enforced it.
        // A silent replacement would change how every future adjustment is computed while leaving
        // the already-confirmed history describing a formula the contract no longer has.
        if (AdjustmentClause is not null && AdjustmentClause.Id != clause.Id)
        {
            throw new InvalidOperationException(
                "This contract already has an adjustment clause; a clause is never reassigned.");
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
        Guid confirmedBy,
        AdjustmentKind kind = AdjustmentKind.Regular,
        Guid? correctsAdjustmentId = null)
    {
        var adjustment = RentAdjustment.Confirm(
            adjustmentId, Id, proposal, confirmedAt, confirmedBy, kind, correctsAdjustmentId);

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
