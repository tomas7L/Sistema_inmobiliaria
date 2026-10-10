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
    /// same date. Two figures, two names, deliberately (spec "Two Figures, Named Apart"). The
    /// second one needs the contract's terms and therefore does not live on this type.
    /// </summary>
    public decimal LedgerBalance(DateOnly asOf) =>
        _movements.Where(m => m.OccurredOn <= asOf).Sum(m => m.Amount);

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
