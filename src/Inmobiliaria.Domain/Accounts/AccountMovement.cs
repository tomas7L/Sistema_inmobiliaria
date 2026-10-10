namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// One line in a contract's ledger. Immutable: no public mutator and no public setter, because the
/// ledger is append-only and a correction is a new movement naming the one it corrects, never an
/// edit (spec "The Ledger Is Append-Only; a Correction Is a New Movement"). A database trigger
/// enforces the same rule against anything that bypasses this type.
/// </summary>
public sealed class AccountMovement
{
    public Guid Id { get; }

    public MovementKind Kind { get; }

    /// <summary>
    /// Signed. A debit is positive — the tenant owes more — and a credit is negative. "Otros
    /// Conceptos" would be either, which is why the sign lives on the amount rather than on the
    /// kind; that line is not in this capability, but the shape is already right for it.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>
    /// The date this movement counts from. For a <see cref="MovementKind.RecargoFrozen"/> it is the
    /// day the payment plan was signed, and that date is the entire point of the movement.
    /// </summary>
    public DateOnly OccurredOn { get; }

    /// <summary>
    /// The rental period this movement belongs to, as the first day of its month. Present on a
    /// <see cref="MovementKind.RentAccrual"/> and absent on everything else — a correction or a
    /// frozen surcharge belongs to the account, not to one month. The database carries a unique
    /// index on (account, kind, period) where this is not null, which is what stops two concurrent
    /// readers materialising the same period twice (design Decision 1).
    /// </summary>
    public DateOnly? Period { get; }

    /// <summary>
    /// Set only on a <see cref="MovementKind.Correction"/>, naming the movement being corrected.
    /// Without it a correction is just an unexplained amount.
    /// </summary>
    public Guid? CorrectsMovementId { get; }

    /// <summary>EF Core materialization only.</summary>
    private AccountMovement()
    {
    }

    private AccountMovement(
        Guid id,
        MovementKind kind,
        decimal amount,
        DateOnly occurredOn,
        DateOnly? period,
        Guid? correctsMovementId)
    {
        Id = id;
        Kind = kind;
        Amount = amount;
        OccurredOn = occurredOn;
        Period = period;
        CorrectsMovementId = correctsMovementId;
    }

    /// <summary>
    /// What a contract already owed at go-live. The amount MAY be zero, for a tenant who is up to
    /// date: recording it still says somebody asserted zero, rather than that nobody looked.
    /// </summary>
    public static AccountMovement OpeningBalance(Guid id, decimal amount, DateOnly occurredOn) =>
        new(id, MovementKind.OpeningBalance, amount, occurredOn, period: null, correctsMovementId: null);

    /// <summary>
    /// One period's rent. The amount must be positive: a period that charges nothing is not a
    /// period, it is a missing accrual, and the two must not look alike in the ledger.
    /// </summary>
    public static AccountMovement RentAccrual(Guid id, decimal amount, DateOnly period, DateOnly dueOn)
    {
        if (amount <= 0m)
        {
            throw new ArgumentException("A rent accrual must be a positive amount.", nameof(amount));
        }

        return new AccountMovement(id, MovementKind.RentAccrual, amount, dueOn, period, correctsMovementId: null);
    }

    /// <summary>One instalment of the tenant's one-off contract commission. Fixed; it never adjusts.</summary>
    public static AccountMovement ContractualFeeInstalment(Guid id, decimal amount, DateOnly dueOn)
    {
        if (amount <= 0m)
        {
            throw new ArgumentException("A fee instalment must be a positive amount.", nameof(amount));
        }

        return new AccountMovement(id, MovementKind.ContractualFeeInstalment, amount, dueOn, period: null, correctsMovementId: null);
    }

    /// <summary>
    /// The surcharge accrued up to the day a payment plan was signed, frozen there by the novación.
    /// </summary>
    public static AccountMovement RecargoFrozen(Guid id, decimal amount, DateOnly signedOn)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("A frozen recargo cannot be negative.", nameof(amount));
        }

        return new AccountMovement(id, MovementKind.RecargoFrozen, amount, signedOn, period: null, correctsMovementId: null);
    }

    /// <summary>
    /// Corrects an earlier movement by the difference. Signed either way, and it must name what it
    /// corrects — an unattributed adjustment is indistinguishable from a second mistake.
    /// </summary>
    /// <param name="period">
    /// The period this correction belongs to, when it corrects something period-scoped such as a
    /// rent accrual. It must be carried, or the corrected period would keep reading as unpaid for
    /// the amount of the mistake.
    /// </param>
    public static AccountMovement Correction(
        Guid id,
        decimal difference,
        DateOnly occurredOn,
        Guid correctsMovementId,
        DateOnly? period = null)
    {
        if (difference == 0m)
        {
            throw new ArgumentException("A correction of zero corrects nothing.", nameof(difference));
        }

        if (correctsMovementId == Guid.Empty)
        {
            throw new ArgumentException("A correction must name the movement it corrects.", nameof(correctsMovementId));
        }

        return new AccountMovement(id, MovementKind.Correction, difference, occurredOn, period, correctsMovementId);
    }
}
