using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// Whether a period can be settled by a payment, and if not, why. A neutral answer from the
/// ledger; collection maps it to its own refusals and operator text.
/// </summary>
public enum PeriodPayability
{
    /// <summary>No live payment settles it, and it is owed or begun and not yet due.</summary>
    Payable,

    /// <summary>A live payment already settles it, or nothing is owed on it.</summary>
    AlreadySettled,

    /// <summary>Its month has not begun.</summary>
    NotBegun,

    /// <summary>
    /// The schedule never produces it and no opening balance carries it, or it is not a month.
    /// </summary>
    NotOnContract,

    /// <summary>A novación froze the recargo, so the account accepts no payment.</summary>
    AccountFrozen,
}

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

        // A pre-existing debt is loaded month by month, so an opening balance is asserted once per
        // month, never once per account (design D18). A non-zero figure without a month could not
        // be settled by any payment, and a month already charged cannot be charged again.
        if (movement.Kind == MovementKind.OpeningBalance)
        {
            if (movement.Period is null)
            {
                if (movement.Amount != 0m)
                {
                    throw new InvalidOperationException(
                        "A non-zero opening balance must carry the period it is owed for.");
                }

                if (_movements.Any(m => m.Kind == MovementKind.OpeningBalance && m.Period is null))
                {
                    throw new InvalidOperationException(
                        "This account already carries a period-less opening balance of zero.");
                }

                // Weak rule: it only prevents a confusing mix of "nothing owed" and monthly figures.
                if (_movements.Any(m => m.Kind == MovementKind.OpeningBalance))
                {
                    throw new InvalidOperationException(
                        "This account already carries monthly opening balances; it cannot also assert zero.");
                }
            }
            else if (_movements.Any(m => m.Kind == MovementKind.OpeningBalance && m.Period is null))
            {
                // The same weak rule read the other way round.
                throw new InvalidOperationException(
                    "This account already asserts a period-less zero; it cannot also carry monthly opening balances.");
            }
            else if (IsChargedFor(movement.Period.Value))
            {
                throw new InvalidOperationException(
                    "This period is already charged; an opening balance would charge it twice.");
            }
        }

        // One charge per period across rent and opening balance. The database's unique index is the
        // backstop for two writers; this is the domain saying the same thing first.
        if (movement.Kind == MovementKind.RentAccrual && IsChargedFor(movement.Period!.Value))
        {
            throw new InvalidOperationException(
                "This period is already charged; it cannot take a second rent accrual.");
        }

        // An instalment is raised once per period (spec Test 58). Same shape as the rent rule above:
        // the database's unique index is the backstop for two writers, this is the domain saying it first.
        if (movement.Kind == MovementKind.ContractualFeeInstalment && HasInstalmentFor(movement.Period!.Value))
        {
            throw new InvalidOperationException(
                "This period already carries its instalment; it cannot take a second one.");
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
    /// An opening balance accrues no surcharge, with a month or without one. There is no due date
    /// to count from, and inventing one would invent a debt. The figure the agency types at
    /// go-live is what their spreadsheet already says is owed, mora included.
    /// </summary>
    public decimal AmountOwed(DateOnly asOf, RecargoTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var ledger = LedgerBalance(asOf);

        if (IsFrozen(asOf))
        {
            return ledger;
        }

        var projected = 0m;

        foreach (var period in _movements.Where(m => m.Period is not null).Select(m => m.Period!.Value).Distinct())
        {
            projected += RecargoProjectedFor(period, asOf, terms);
        }

        return ledger + projected;
    }

    /// <summary>
    /// The recargo projected on one period as of <paramref name="asOf"/>: what <see cref="AmountOwed"/>
    /// adds for that period, extracted so collection can charge exactly the figure the amount owed
    /// showed (design D22). Pure, and no behaviour change from the loop it was lifted out of.
    ///
    /// Zero when the period nets to nothing owed, so a settled period projects nothing and a paid
    /// period never charges twice. Zero for a month carried by an opening balance, because only a
    /// rent accrual establishes when a period fell due: the typed figure already includes whatever
    /// mora the agency's spreadsheet showed (spec Decision 31). Zero on a frozen account, whose
    /// surcharge stopped on the novación date and is already written as a movement.
    /// </summary>
    public decimal RecargoProjectedFor(DateOnly period, DateOnly asOf, RecargoTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        if (IsFrozen(asOf))
        {
            return 0m;
        }

        // Dated the same way the ledger balance is, so the two figures always describe the same
        // moment.
        var owing = PeriodNet(period, asOf);
        if (owing <= 0m)
        {
            return 0m;
        }

        // Only an accrual establishes when a period fell due. A period holding corrections or an
        // opening balance but no accrual still counts its principal in the ledger; it simply has no
        // date from which a surcharge could be measured.
        var accrual = _movements.FirstOrDefault(m => m.Kind == MovementKind.RentAccrual && m.Period == period);
        if (accrual is null)
        {
            return 0m;
        }

        return RecargoMath.For(owing, accrual.OccurredOn, asOf, terms);
    }

    /// <summary>
    /// The sum of everything this account holds for <paramref name="period"/> dated on or before
    /// <paramref name="asOf"/>. Zero means the period is settled; above zero means it is owed.
    /// </summary>
    public decimal PeriodNet(DateOnly period, DateOnly asOf) =>
        _movements.Where(m => m.Period == period && m.OccurredOn <= asOf).Sum(m => m.Amount);

    /// <summary>
    /// The sum of everything this account holds for <paramref name="period"/>, whatever its date: the
    /// period AS IT STANDS. Whether a period is owed, settled or payable is read from this figure and
    /// not from <see cref="PeriodNet(DateOnly, DateOnly)"/>, because a movement dated after the
    /// received date (an opening balance typed after the money arrived, the corrections of a voiding
    /// made later) is still part of the account (cobranza-y-recibos Issues found 8).
    /// </summary>
    public decimal PeriodNet(DateOnly period) =>
        _movements.Where(m => m.Period == period).Sum(m => m.Amount);

    /// <summary>
    /// The recargo a payment received on <paramref name="receivedOn"/> charges on
    /// <paramref name="period"/>: measured to the received date, on the period as it stands (so a
    /// payment voided on a later day is as if it never existed). Differs from
    /// <see cref="RecargoProjectedFor"/>, which dates the principal too and is what
    /// <see cref="AmountOwed"/> adds. Zero for a month carried by an opening balance (spec Decision
    /// 31) and on a frozen account.
    /// </summary>
    public decimal RecargoProjectedForPayment(DateOnly period, DateOnly receivedOn, RecargoTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        if (IsFrozen(receivedOn))
        {
            return 0m;
        }

        var owing = PeriodNet(period);
        if (owing <= 0m)
        {
            return 0m;
        }

        var accrual = _movements.FirstOrDefault(m => m.Kind == MovementKind.RentAccrual && m.Period == period);

        return accrual is null ? 0m : RecargoMath.For(owing, accrual.OccurredOn, receivedOn, terms);
    }

    /// <summary>
    /// What the account owes AS IT STANDS (every movement, whatever its date) with the recargo measured
    /// to <paramref name="receivedOn"/>: the basis a payment received that day charges on, so a preview
    /// built from it agrees with the figure charged. Not <see cref="AmountOwed"/>, which dates the
    /// principal too and stays the figure of the mora worklist. Frozen as of the received date, the
    /// recargo is already written and nothing more is projected.
    /// </summary>
    public decimal AmountOwedForPayment(DateOnly receivedOn, RecargoTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var ledger = _movements.Sum(m => m.Amount);

        if (IsFrozen(receivedOn))
        {
            return ledger;
        }

        var projected = _movements
            .Where(m => m.Period is not null)
            .Select(m => m.Period!.Value)
            .Distinct()
            .Sum(period => RecargoProjectedForPayment(period, receivedOn, terms));

        return ledger + projected;
    }

    /// <summary>
    /// Whether a novación froze the recargo on or before <paramref name="asOf"/>. A frozen account
    /// projects nothing further and accepts no payment.
    /// </summary>
    public bool IsFrozen(DateOnly asOf) =>
        _movements.Any(m => m.Kind == MovementKind.RecargoFrozen && m.OccurredOn <= asOf);

    /// <summary>
    /// The earliest period whose movements do not net to zero, or null when the account is clear.
    /// This is the account's own answer to "which period is oldest unpaid", so every caller gets
    /// the same one instead of re-deriving it. It is the DEFAULT a payment settles when the tenant
    /// names no period; the tenant may name another one (spec "A Payment Settles the Period the
    /// Tenant Names, or the Oldest Unpaid One"). <see cref="DefaultPeriodToPay"/> is the answer
    /// collection uses, which also takes the date and the contract into account.
    ///
    /// "Unpaid" is the NET of everything carrying that period, not the absence of a payment row.
    /// Netting needs no particular kind: the day a payment lands against a period, that period's
    /// sum reaches zero and it stops being returned here, with nothing in this method changing.
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
    /// Whether this account holds an instalment for <paramref name="period"/>. A payment that
    /// brings a period's rent onto the account asks this before writing the instalment too, so an
    /// instalment is never raised twice.
    /// </summary>
    public bool HasInstalmentFor(DateOnly period) =>
        _movements.Any(m => m.Kind == MovementKind.ContractualFeeInstalment && m.Period == period);

    /// <summary>
    /// Whether a payment for <paramref name="period"/> is still standing: a
    /// <see cref="MovementKind.Payment"/> that no <see cref="MovementKind.Correction"/> names. A
    /// voided payment reverses it and the period can be paid again; the attempt rule in the
    /// database is the same idea enforced against concurrent writers.
    /// </summary>
    public bool HasLivePaymentFor(DateOnly period) =>
        _movements.Any(payment =>
            payment.Kind == MovementKind.Payment &&
            payment.Period == period &&
            !_movements.Any(c => c.Kind == MovementKind.Correction && c.CorrectsMovementId == payment.Id));

    /// <summary>
    /// Whether <paramref name="period"/> can be paid as of <paramref name="asOf"/> (the received
    /// date). Payable means: no live payment settles it, and it is owed, or its month has begun and
    /// its due date has not arrived. It never asks whether the period is already charged, because
    /// a payment voided before the due date leaves a charged period that nets to zero and is
    /// payable again (design section 1).
    ///
    /// "On the contract" is a period the accrual schedule produces or one carrying a movement such
    /// as an opening balance (design D24), so a calendar month the contract never charges is not
    /// on it. The caller materialises due accruals in memory first, so an owed
    /// period is already on the account. Pure: nothing is appended.
    /// </summary>
    public PeriodPayability PayabilityOf(DateOnly period, DateOnly asOf, Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (IsFrozen(asOf))
        {
            return PeriodPayability.AccountFrozen;
        }

        if (period.Day != 1)
        {
            return PeriodPayability.NotOnContract;
        }

        if (period > asOf)
        {
            return PeriodPayability.NotBegun;
        }

        var scheduled = AccrualSchedule.PeriodsBegun(contract, asOf).FirstOrDefault(p => p.Period == period);
        var carried = _movements.Any(m => m.Period == period);

        if (scheduled is null && !carried)
        {
            return PeriodPayability.NotOnContract;
        }

        if (HasLivePaymentFor(period))
        {
            return PeriodPayability.AlreadySettled;
        }

        var owed = PeriodNet(period) > 0m;
        var begunNotYetDue = scheduled is not null && scheduled.DueDate > asOf;

        return owed || begunNotYetDue ? PeriodPayability.Payable : PeriodPayability.AlreadySettled;
    }

    /// <summary>
    /// The period a payment settles when the tenant names none, as of <paramref name="asOf"/>: the
    /// oldest payable period that is owed (opening-balance months included, they are due from the
    /// moment the account exists); otherwise the month in course, when it has begun and is payable;
    /// otherwise null, nothing is payable (design section 2, "Picking the period"). Makes the
    /// tenant pay the most expensive month first, which is the agency's practice and only a default.
    /// </summary>
    public DateOnly? DefaultPeriodToPay(DateOnly asOf, Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var oldestOwed = _movements
            .Where(m => m.Period is not null)
            .Select(m => m.Period!.Value)
            .Distinct()
            .Where(period => PeriodNet(period) > 0m)
            .OrderBy(period => period)
            .Cast<DateOnly?>()
            .FirstOrDefault(period => PayabilityOf(period!.Value, asOf, contract) == PeriodPayability.Payable);

        if (oldestOwed is not null)
        {
            return oldestOwed;
        }

        var inCourse = new DateOnly(asOf.Year, asOf.Month, 1);

        return PayabilityOf(inCourse, asOf, contract) == PeriodPayability.Payable ? inCourse : null;
    }

    /// <summary>
    /// Whether <paramref name="period"/> already carries its charge: a rent accrual, or an opening
    /// balance typed for that month. At most one of the two may ever exist for a period.
    /// </summary>
    public bool IsChargedFor(DateOnly period) =>
        _movements.Any(m =>
            m.Period == period &&
            m.Kind is MovementKind.RentAccrual or MovementKind.OpeningBalance);

    /// <summary>
    /// Whether this account already holds an accrual for <paramref name="period"/>. Materialisation
    /// asks this before writing, and the database's unique index is the backstop for two readers
    /// asking at the same moment (design Decision 1).
    /// </summary>
    public bool HasAccrualFor(DateOnly period) =>
        _movements.Any(m => m.Kind == MovementKind.RentAccrual && m.Period == period);
}
