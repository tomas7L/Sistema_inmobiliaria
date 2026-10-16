namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// One printed line of a recibo. <paramref name="Position"/> is 1-based and follows the print order of
/// <see cref="ReciboLineKind"/>; the amount is signed and already truncated to whole pesos.
/// </summary>
public sealed record ReciboLine(ReciboLineKind Kind, int Position, decimal Amount);

/// <summary>
/// The behaviour each <see cref="ReciboLineKind"/> carries (design D11): which lines the ledger
/// derives and the operator cannot override, which copy shows them, and which may be negative.
/// </summary>
public static class ReciboLineCatalog
{
    /// <summary>
    /// True for a line computed from the ledger. The operator never types one, so no member of
    /// <see cref="TypedReciboLines"/> corresponds to it.
    /// </summary>
    public static bool IsLedgerDerived(ReciboLineKind kind) => kind is
        ReciboLineKind.Rent or
        ReciboLineKind.ContractualFeeInstalment or
        ReciboLineKind.Recargo or
        ReciboLineKind.SaldoDeudor;

    /// <summary>
    /// Whether a kind may be printed on a copy. The instalment is the tenant's alone; the
    /// honorarios line is the lessor's alone; everything else is on both.
    /// </summary>
    public static bool AppearsOn(ReciboLineKind kind, ReciboCopy copy) => kind switch
    {
        ReciboLineKind.ContractualFeeInstalment => copy == ReciboCopy.Locatario,
        ReciboLineKind.HonorariosAdministracion => copy == ReciboCopy.Propietario,
        _ => true,
    };

    /// <summary>
    /// Whether an amount below zero is allowed for a kind. Seguro and Otros Conceptos are the
    /// operator's credits; honorarios is always zero or negative, which <see cref="ReciboDraft"/>
    /// enforces on top of this.
    /// </summary>
    public static bool MayBeNegative(ReciboLineKind kind) => kind is
        ReciboLineKind.Seguro or
        ReciboLineKind.OtrosConceptos or
        ReciboLineKind.HonorariosAdministracion;
}
