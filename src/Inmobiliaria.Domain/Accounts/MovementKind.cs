namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// What a single line in a contract's ledger means. The kinds differ in meaning, not in shape,
/// which is why they share one table behind a discriminator rather than having one each
/// (design Decision 3).
/// </summary>
public enum MovementKind
{
    /// <summary>
    /// What the tenant already owed when the system went live. Typed by a person, never computed,
    /// and present only on a contract that predates the system — a contract created here starts
    /// with an empty account (spec "An Opening Balance Exists Only for a Contract That Predates
    /// the System").
    /// </summary>
    OpeningBalance,

    /// <summary>
    /// One period's rent, at the canon in force for that period. Materialised on demand rather
    /// than written by a monthly job (design Decision 1), and never re-priced afterwards, because
    /// a late adjustment generates no retroactive charge.
    /// </summary>
    RentAccrual,

    /// <summary>
    /// One instalment of the honorarios contractuales — the one-off commission the TENANT pays at
    /// signing, in 1 to 6 FIXED instalments. Not to be confused with honorarios de administración,
    /// the monthly percentage the OWNER pays, which never reaches this ledger.
    /// </summary>
    ContractualFeeInstalment,

    /// <summary>
    /// The recargo accrued up to the day a payment plan was signed. Signing is a *novación*: the
    /// original obligation is extinguished and replaced, so the surcharge stops on that date. It is
    /// a dated movement and never a flag, because a flag cannot answer "frozen since when" and the
    /// date is the whole of the legal effect (spec "A Novación Freezes the Recargo on a Dated
    /// Movement").
    /// </summary>
    RecargoFrozen,

    /// <summary>
    /// Corrects an earlier movement by appending the difference and naming it. The ledger is
    /// append-only: a mistake is answered with a new line, never by editing the old one.
    /// </summary>
    Correction,
}
