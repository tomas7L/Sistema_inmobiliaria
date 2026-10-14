namespace Inmobiliaria.Domain.Collection;

/// <summary>Which of the two paper copies a recibo is. One payment issues one of each.</summary>
public enum ReciboCopy
{
    /// <summary>The tenant's copy.</summary>
    Locatario,

    /// <summary>The lessor's copy.</summary>
    Propietario,
}

/// <summary>
/// The kinds of line a recibo can carry. The declaration order IS the print order: a draft lists its
/// lines in this order and numbers their positions from one. The behaviour of each kind (derived or
/// typed, which copy, may it be negative) lives in <see cref="ReciboLineCatalog"/>, because a table
/// row could not hold it (design D11). Persisted later as a string with a CHECK list that mirrors
/// this enum, so do not reorder or rename members without a migration.
/// </summary>
public enum ReciboLineKind
{
    /// <summary>The period's rent, from the ledger.</summary>
    Rent,

    /// <summary>The honorarios contractuales instalment riding with the rent. Locatario copy only.</summary>
    ContractualFeeInstalment,

    /// <summary>The late-payment surcharge, from the ledger. Absent when it is zero.</summary>
    Recargo,

    /// <summary>Municipal rate, typed by the operator. Never negative.</summary>
    TasaMunicipal,

    /// <summary>Insurance, typed by the operator. May be negative.</summary>
    Seguro,

    /// <summary>Anything else, typed by the operator. May be negative.</summary>
    OtrosConceptos,

    /// <summary>
    /// The figure of a month loaded as an opening balance, printed in place of rent (design D27).
    /// </summary>
    SaldoDeudor,

    /// <summary>
    /// The agency's administration fee, always zero or negative. Propietario copy only. Stored when the
    /// recibo is issued and never recomputed.
    /// </summary>
    HonorariosAdministracion,
}
