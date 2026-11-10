namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// The figures the ledger already holds for the period being paid, which the operator cannot change.
/// Either an ordinary rent period (rent, optionally an instalment, a recargo) or a month loaded as an
/// opening balance (one figure, printed as Saldo Deudor, with no rent, instalment or recargo,
/// spec Decision 31). Amounts are whole pesos, as the ledger writes them.
/// </summary>
public sealed record ReciboLedgerFigures
{
    public decimal? Rent { get; }

    public decimal? ContractualFeeInstalment { get; }

    public decimal Recargo { get; }

    public decimal? SaldoDeudor { get; }

    /// <summary>A late adjustment's difference charged on this period, or null (gestion-de-alquileres spec Requirement 20).</summary>
    public decimal? Difference { get; }

    private ReciboLedgerFigures(decimal? rent, decimal? instalment, decimal recargo, decimal? saldoDeudor, decimal? difference = null)
    {
        Rent = rent;
        ContractualFeeInstalment = instalment;
        Recargo = recargo;
        SaldoDeudor = saldoDeudor;
        Difference = difference;
    }

    public static ReciboLedgerFigures ForRentPeriod(decimal rent, decimal? contractualFeeInstalment, decimal recargo, decimal? difference = null)
    {
        ThrowIfNotPositive(difference, nameof(difference));

        ArgumentOutOfRangeException.ThrowIfNegative(rent);
        ArgumentOutOfRangeException.ThrowIfNegative(recargo);
        ThrowIfFractional(rent, nameof(rent));
        ThrowIfFractional(recargo, nameof(recargo));

        if (contractualFeeInstalment is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contractualFeeInstalment), "An instalment is never negative.");
        }

        if (contractualFeeInstalment is { } instalment)
        {
            ThrowIfFractional(instalment, nameof(contractualFeeInstalment));
        }

        return new ReciboLedgerFigures(rent, contractualFeeInstalment, recargo, saldoDeudor: null, difference);
    }

    /// <summary>
    /// A period that holds only a late adjustment's difference, because its contract ended before it accrued: no rent line and
    /// no recargo (spec Requirement 19).
    /// </summary>
    public static ReciboLedgerFigures ForDifferenceOnly(decimal difference)
    {
        ThrowIfNotPositive(difference, nameof(difference));

        return new ReciboLedgerFigures(rent: null, instalment: null, recargo: 0m, saldoDeudor: null, difference);
    }

    private static void ThrowIfNotPositive(decimal? value, string paramName)
    {
        if (value is { } amount)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount, paramName);
            ThrowIfFractional(amount, paramName);
        }
    }

    public static ReciboLedgerFigures ForOpeningBalance(decimal figure)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(figure);
        ThrowIfFractional(figure, nameof(figure));

        return new ReciboLedgerFigures(rent: null, instalment: null, recargo: 0m, saldoDeudor: figure);
    }

    // Ledger figures are whole pesos upstream; a fraction means a bug there, so refuse it, never cut it.
    private static void ThrowIfFractional(decimal value, string paramName)
    {
        if (value != decimal.Truncate(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "A ledger figure is whole pesos.");
        }
    }

    /// <summary>
    /// What the honorarios percentage applies to: rent plus recargo plus a late adjustment's difference, or the opening-balance
    /// figure itself, never the whole loaded debt (design D25, D27; gestion-de-alquileres spec Decision 20). The instalment is
    /// not in it.
    /// </summary>
    internal decimal HonorariosBase => SaldoDeudor ?? ((Rent ?? 0m) + Recargo + (Difference ?? 0m));
}

/// <summary>
/// The three lines an operator types. There is deliberately no member for a ledger-derived line, so
/// a derived figure cannot be overridden (spec Test 37). Null means "not typed".
/// </summary>
public sealed record TypedReciboLines(decimal? TasaMunicipal = null, decimal? Seguro = null, decimal? OtrosConceptos = null)
{
    public static TypedReciboLines None { get; } = new();
}

/// <summary>
/// Builds both copies of a recibo from ledger figures, typed lines and the contract's administration
/// percentage. Pure: it reads no account and appends nothing, so the typed lines and the honorarios
/// line can never reach the ledger (spec Tests 41 and 46).
/// </summary>
public static class ReciboBuilder
{
    public static ReciboDraftPair Build(
        ReciboLedgerFigures ledger,
        TypedReciboLines typed,
        decimal honorariosPercentage)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentOutOfRangeException.ThrowIfLessThan(honorariosPercentage, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(honorariosPercentage, 100m);

        // The refusal looks at what the operator typed, before truncation: -0.5 is a negative tasa.
        if (typed.TasaMunicipal is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(typed), "A negative tasa municipal is refused.");
        }

        var amounts = new SortedDictionary<ReciboLineKind, decimal>();

        Add(amounts, ReciboLineKind.Rent, ledger.Rent);
        Add(amounts, ReciboLineKind.ContractualFeeInstalment, ledger.ContractualFeeInstalment);

        if (ledger.Recargo > 0m)
        {
            amounts[ReciboLineKind.Recargo] = ledger.Recargo;
        }

        Add(amounts, ReciboLineKind.AdjustmentDifference, ledger.Difference);

        // Typed amounts are truncated toward zero on entry; one that truncates to zero prints nothing.
        AddTyped(amounts, ReciboLineKind.TasaMunicipal, typed.TasaMunicipal);
        AddTyped(amounts, ReciboLineKind.Seguro, typed.Seguro);
        AddTyped(amounts, ReciboLineKind.OtrosConceptos, typed.OtrosConceptos);

        Add(amounts, ReciboLineKind.SaldoDeudor, ledger.SaldoDeudor);

        var locatario = new ReciboDraft(ReciboCopy.Locatario, ToLines(amounts, ReciboCopy.Locatario));

        // Stored as issued, never recomputed. Truncate the positive fee first, then negate, so the
        // cut is toward zero (47,860.80 is -47,860, never -47,861). Printed even when the rate is 0.
        // A zero fee is 0m and never negative zero: unary minus on 0m sets the sign bit (R3-001).
        var fee = decimal.Truncate(honorariosPercentage / 100m * ledger.HonorariosBase);
        amounts[ReciboLineKind.HonorariosAdministracion] = fee == 0m ? 0m : -fee;

        var propietario = new ReciboDraft(ReciboCopy.Propietario, ToLines(amounts, ReciboCopy.Propietario));

        return new ReciboDraftPair(locatario, propietario);
    }

    private static void Add(SortedDictionary<ReciboLineKind, decimal> amounts, ReciboLineKind kind, decimal? amount)
    {
        if (amount is { } value)
        {
            amounts[kind] = value;
        }
    }

    private static void AddTyped(SortedDictionary<ReciboLineKind, decimal> amounts, ReciboLineKind kind, decimal? typed)
    {
        if (typed is { } value && decimal.Truncate(value) != 0m)
        {
            amounts[kind] = decimal.Truncate(value);
        }
    }

    private static List<ReciboLine> ToLines(SortedDictionary<ReciboLineKind, decimal> amounts, ReciboCopy copy)
    {
        var lines = new List<ReciboLine>();

        foreach (var (kind, amount) in amounts)
        {
            if (ReciboLineCatalog.AppearsOn(kind, copy))
            {
                lines.Add(new ReciboLine(kind, lines.Count + 1, amount));
            }
        }

        return lines;
    }
}
