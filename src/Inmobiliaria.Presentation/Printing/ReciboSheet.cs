namespace Inmobiliaria.Presentation.Printing;

/// <summary>
/// One recibo printed twice (spec Decisions 15, 16): ORIGINAL and DUPLICADO with identical content, differing
/// only in their caption. A payment's two recibos are two sheets. Pure data, built in Presentation and drawn by
/// Desktop.
/// </summary>
public sealed record ReciboSheet(string NumberText, ReciboHalf Original, ReciboHalf Duplicado)
{
    /// <summary>The paper's black band, the agency's fixed template text (not a letterhead item).</summary>
    public const string ServiceBand = "COBRO de ALQUILER/SERVICIOS e IMPUESTOS por CUENTA y ORDEN de TERCEROS";

    /// <summary>The line under the names on the paper, fixed template text as well.</summary>
    public const string CobroLine = "COBRO: alquiler, tasas, servicios e Impuestos por su cuenta";

    /// <summary>The paper's I.V.A. boxes, printed unticked: the system does not record the payer's condition.</summary>
    public static readonly IReadOnlyList<string> IvaBoxLabels =
        ["Resp. Insc.", "Resp. Monotributo", "No Resp.", "Cons. Final", "Exento"];
}

/// <summary>One half of a sheet: its caption and the content both halves share.</summary>
public sealed record ReciboHalf(string Caption, ReciboHalfContent Content);

/// <summary>Everything a half prints. Beyond the stored recibo it adds only what Requirement 24 allows.</summary>
public sealed record ReciboHalfContent(
    AgencyDetails Agency,
    string Title,
    string NumberText,
    string RecipientType,
    string DateText,
    string NonFiscalLegend,
    string ServiceBand,
    string Tenant,
    string Lessors,
    string Units,
    string CobroLine,
    IReadOnlyList<PrintedRow> Rows,
    string ImporteNetoText,
    string AmountInWordsText,
    string PayerLine,
    IReadOnlyList<string> IvaBoxes,
    string RentalActivityLegend,
    string BrokerName,
    string BrokerTitle,
    string LicenceText,
    PrintedAnnulment? Annulment);

/// <summary>The ANULADO block: read from the database at print time (D13), so a later voiding is always marked.</summary>
public sealed record PrintedAnnulment(string Word, string DateText, string Reason);
