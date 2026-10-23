using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Presentation.CashDesk;

/// <summary>
/// The cash desk's fixed operator texts. The ledger figure and the recibo totals carry different names on
/// purpose (spec Requirement 8, Decision 25): the handed amount is compared with the first, never the second.
/// </summary>
public static class DeskTexts
{
    public const string LedgerFigureLabel = "Saldo del período";

    public const string LocatarioTotalLabel = "Total del recibo locatario";

    public const string PropietarioTotalLabel = "Total del recibo propietario";

    public const string HandedAmountLabel =
        "Importe entregado por el inquilino (se compara con el saldo del período: alquiler, cuota y recargo)";

    public const string ZeroAdministrationRate =
        "El contrato cobra 0% de honorarios de administración. Verifique que sea correcto antes de cobrar.";

    /// <summary>The pending-adjustment check could not run; the payment is not blocked, but the operator is told.</summary>
    public const string AdjustmentCheckFailed =
        "No se pudo verificar si el contrato tiene un ajuste de alquiler pendiente. Revíselo antes de cobrar.";

    public static string Warning(CollectionWarning warning) => warning switch
    {
        CollectionWarning.ZeroAdministrationRate => ZeroAdministrationRate,
        _ => throw new ArgumentOutOfRangeException(nameof(warning), warning, "A warning without operator text."),
    };
}
