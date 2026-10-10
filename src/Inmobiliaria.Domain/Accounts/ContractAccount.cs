namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// The running account of one contract: an append-only ledger whose movements answer what is owed
/// and why.
///
/// It belongs to a CONTRACT and never to a tenant (spec "An Account Belongs to Exactly One
/// Contract"). Rent is owed under a contract, at a canon that contract sets, from a due day that
/// contract sets; two contracts late by different amounts cannot share one clock. "What does this
/// tenant owe" is a sum over that tenant's contracts, computed when asked and never stored.
///
/// The account OUTLIVES its contract. Ending a contract neither closes nor freezes it: a contract
/// can be rescinded while money is still owed, and payments keep landing until the balance reaches
/// zero (spec "The Account Outlives Its Contract"). There is deliberately no closed flag — a
/// contract past its end date simply has no further period coming due.
/// </summary>
public sealed class ContractAccount
{
    private readonly List<AccountMovement> _movements = [];

    public Guid Id { get; }

    public Guid ContractId { get; }

    public IReadOnlyCollection<AccountMovement> Movements => _movements;

    /// <summary>EF Core materialization only; EF rebuilds the movements from their persisted rows.</summary>
    private ContractAccount()
    {
    }

    /// <summary>
    /// An account is born empty. That is the ordinary case — a contract signed today has nothing to
    /// carry over — and an earlier draft of the specification wrongly demanded an opening balance of
    /// every account, which would have made it impossible to register a tenant who signs tomorrow.
    /// </summary>
    public ContractAccount(Guid id, Guid contractId)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException("An account must belong to a contract.", nameof(contractId));
        }

        Id = id;
        ContractId = contractId;
    }

    /// <summary>
    /// Appends a movement. The only way to change an account: nothing here updates or removes.
    /// </summary>
    public void Append(AccountMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);

        // At most one opening balance, ever. A second one would silently restate what the agency
        // asserted at go-live, and the ledger would no longer reconcile against their spreadsheet.
        if (movement.Kind == MovementKind.OpeningBalance &&
            _movements.Any(m => m.Kind == MovementKind.OpeningBalance))
        {
            throw new InvalidOperationException(
                "This account already carries an opening balance; it is asserted once and never restated.");
        }

        // A correction must point at a movement this account actually holds. Pointing elsewhere
        // would let one account silently adjust another's history.
        if (movement.Kind == MovementKind.Correction &&
            !_movements.Any(m => m.Id == movement.CorrectsMovementId))
        {
            throw new InvalidOperationException(
                "A correction must name a movement of this same account.");
        }

        _movements.Add(movement);
    }

    /// <summary>
    /// The sum of the movements dated on or before <paramref name="asOf"/> — what is WRITTEN.
    ///
    /// This is NOT what the tenant owes: recargo is derived and is not a movement until it is
    /// frozen or charged, so the amount owed is this figure plus the surcharge projected to the
    /// same date. Two figures, two names, deliberately (spec "Two Figures, Named Apart"), and
    /// <see cref="AmountOwed"/> is the other one — kept beside this method so the distinction is
    /// visible at the point where somebody might reach for the wrong figure.
    /// </summary>
    public decimal LedgerBalance(DateOnly asOf) =>
        _movements.Where(m => m.OccurredOn <= asOf).Sum(m => m.Amount);

    /// <summary>
    /// What the tenant actually owes as of <paramref name="asOf"/>: the ledger balance plus the
    /// recargo projected to that same date. The figure an operator acts on (spec "Two Figures,
    /// Named Apart").
    ///
    /// Each unpaid period counts from its OWN due date, never from one clock for the whole
    /// account: after July is settled, August and September each keep counting from their own
    /// tenth. That due date is the accrual movement's own date, so the account needs nothing but
    /// itself and the terms to answer.
    ///
    /// Pure, and it writes nothing. A caller that wants periods materialised first says so, via
    /// <see cref="IAccountMaterialiser"/>.
    ///
    /// Two deliberate silences:
    ///
    /// A frozen account projects NOTHING further. A novación extinguished the obligation, and the
    /// surcharge it had accrued is already written as a movement — so continuing to project would
    /// both run a clock that stopped and count the freeze twice.
    ///
    /// A movement carrying no period accrues no surcharge, the opening balance above all. There
    /// is no due date to count from, and inventing one would invent a debt. The figure the agency
    /// types at go-live is what their spreadsheet already says is owed, mora included.
    /// </summary>
    public decimal AmountOwed(DateOnly asOf, RecargoTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var ledger = LedgerBalance(asOf);

        if (_movements.Any(m => m.Kind == MovementKind.RecargoFrozen && m.OccurredOn <= asOf))
        {
            return ledger;
        }

        var projected = 0m;

        foreach (var period in _movements.Where(m => m.Period is not null).GroupBy(m => m.Period!.Value))
        {
            // Dated the same way the ledger balance is, so the two figures always describe the
            // same moment.
            var owing = period.Where(m => m.OccurredOn <= asOf).Sum(m => m.Amount);
            if (owing <= 0m)
            {
                continue;
            }

            // Only an accrual establishes when a period fell due. A period holding corrections
            // but no accrual still counts its principal in the ledger above; it simply has no
            // date from which a surcharge could be measured.
            var accrual = period.FirstOrDefault(m => m.Kind == MovementKind.RentAccrual);
            if (accrual is null)
            {
                continue;
            }

            projected += RecargoMath.For(owing, accrual.OccurredOn, asOf, terms);
        }

        return ledger + projected;
    }

    /// <summary>
    /// The earliest period whose movements do not net to zero, or null when the account is clear.
    /// A payment always settles the oldest unpaid period and the operator never chooses (spec
    /// "A Payment Settles the Oldest Unpaid Period"); keeping that rule here means every caller
    /// gets the same answer instead of re-deriving it.
    ///
    /// "Unpaid" is the NET of everything carrying that period, not the absence of a payment row.
    /// That matters because the payment movement does not exist yet — collection adds it — and a
    /// rule written around a kind that is still missing would be a rule nobody could test. Netting
    /// needs no new kind: the day a payment lands against a period, that period's sum reaches zero
    /// and it stops being returned here, with nothing in this method changing.
    /// </summary>
    public DateOnly? OldestUnpaidPeriod() =>
        _movements
            .Where(m => m.Period is not null)
            .GroupBy(m => m.Period!.Value)
            .Where(group => group.Sum(m => m.Amount) > 0m)
            .Select(group => group.Key)
            .OrderBy(period => period)
            .Cast<DateOnly?>()
            .FirstOrDefault();

    /// <summary>
    /// Whether this account already holds an accrual for <paramref name="period"/>. Materialisation
    /// asks this before writing, and the database's unique index is the backstop for two readers
    /// asking at the same moment (design Decision 1).
    /// </summary>
    public bool HasAccrualFor(DateOnly period) =>
        _movements.Any(m => m.Kind == MovementKind.RentAccrual && m.Period == period);
}
