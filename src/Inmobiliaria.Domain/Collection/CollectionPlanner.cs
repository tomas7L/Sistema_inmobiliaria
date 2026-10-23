using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// What the operator asks for. There is deliberately no recorded-at member: when a collection was
/// recorded is stamped by the database, never supplied (spec Test 11), and no way to name an
/// instalment on its own, because an instalment is paid with its rent (spec Test 58).
/// </summary>
/// <param name="NamedPeriod">The month to settle, or null for the default (design section 2).</param>
/// <param name="ReceivedOn">The day the money arrived, or null for today.</param>
/// <param name="HandedAmount">What the tenant handed over for the ledger figure; it must equal it exactly (spec Decision 27).</param>
public sealed record CollectionRequest(
    Guid AccountId,
    DateOnly? NamedPeriod,
    DateOnly? ReceivedOn,
    decimal HandedAmount,
    decimal? TasaMunicipal = null,
    decimal? Seguro = null,
    decimal? OtrosConceptos = null);

/// <summary>Something the operator should see before confirming, which does not stop the payment.</summary>
public enum CollectionWarning
{
    /// <summary>The contract charges a 0% administration fee: a forgotten rate is caught here (spec Decision 22).</summary>
    ZeroAdministrationRate,
}

/// <summary>Why a collection cannot be committed (design section 2).</summary>
public enum CollectionRefusal
{
    /// <summary>The amount handed over differs from the ledger figure: partial or excess (spec Decision 27).</summary>
    AmountNotTotal,

    PeriodAlreadySettled,
    PeriodNotBegun,
    PeriodNotOnContract,
    NothingPayable,
    AccountFrozen,
    RateNeverEntered,
    ReceivedOnInFuture,
    ReceivedOnBeyondBound,
    NegativeTasaMunicipal,

    /// <summary>
    /// The recibo counter has no starting number yet, so no recibo can be numbered. Raised by the desk at the
    /// commit, which reads the counter. The member keeps its historical name (ui-de-cobranza design section 6.4).
    /// </summary>
    CutoverNotSet,

    /// <summary>The commit's fresh preview differs from the one the operator confirmed (design D21).</summary>
    PreviewStale,

    /// <summary>A concurrent writer got there first. Raised by the desk from the database's answer.</summary>
    ConcurrentChange,
}

/// <summary>The operator-facing text of each refusal. Spanish, because the operator reads it.</summary>
public static class CollectionRefusalText
{
    public static string OperatorText(this CollectionRefusal refusal) => refusal switch
    {
        CollectionRefusal.AmountNotTotal =>
            "El importe entregado no coincide con el total del período. El cobro debe ser total.",
        CollectionRefusal.PeriodAlreadySettled =>
            "El período ya está cobrado o no tiene saldo pendiente.",
        CollectionRefusal.PeriodNotBegun =>
            "El mes elegido todavía no comenzó.",
        CollectionRefusal.PeriodNotOnContract =>
            "El período elegido no corresponde a este contrato.",
        CollectionRefusal.NothingPayable =>
            "No hay ningún período para cobrar.",
        CollectionRefusal.AccountFrozen =>
            "La cuenta está bajo un plan de pago (novación) y no admite cobros.",
        CollectionRefusal.RateNeverEntered =>
            "El contrato no tiene cargada la tasa de honorarios de administración. Cárguela antes de cobrar.",
        CollectionRefusal.ReceivedOnInFuture =>
            "La fecha de cobro no puede ser futura.",
        CollectionRefusal.ReceivedOnBeyondBound =>
            $"La fecha de cobro no puede ser anterior a {CollectionRules.BackDateBoundDays} días atrás.",
        CollectionRefusal.NegativeTasaMunicipal =>
            "La tasa municipal no puede ser negativa.",
        CollectionRefusal.CutoverNotSet =>
            "El número inicial de recibo no está cargado. Un Administrador debe cargarlo en Puesta en marcha antes de cobrar.",
        CollectionRefusal.PreviewStale =>
            "La cuenta cambió desde la vista previa. Genere una nueva vista previa.",
        CollectionRefusal.ConcurrentChange =>
            "Otro usuario modificó la cuenta al mismo tiempo. Genere una nueva vista previa.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };
}

/// <summary>
/// Everything the operator confirms, as a value (design D21): the commit rebuilds it from fresh data
/// and requires equality, which is how another payment, a day turning over or a rate change are all
/// caught by one comparison. A refused preview carries what was resolved before the refusal and
/// leaves the rest at zero or null; it can never be committed.
/// </summary>
/// <param name="Period">The month being settled, or null when none could be resolved.</param>
/// <param name="ReceivedOn">The day the money arrived (today, when the request named none).</param>
/// <param name="ResolvedToday">The agency's today when the preview was built, so a day turning over is a difference.</param>
/// <param name="PeriodWasEarly">True when the period's due date has not arrived: the payment brings its charge onto the ledger (design D1).</param>
/// <param name="PreviousAmountOwed">What the account owed as of the received date, before this payment.</param>
/// <param name="LedgerFigure">Rent + instalment + recargo: what the payment settles and the ledger records. Not the recibo total (spec Decision 27).</param>
public sealed record CollectionPreview(
    DateOnly? Period,
    DateOnly ReceivedOn,
    DateOnly ResolvedToday,
    bool PeriodWasEarly,
    decimal PreviousAmountOwed,
    decimal LedgerFigure,
    decimal ResultingAmountOwed,
    ReciboDraft? Locatario,
    ReciboDraft? Propietario,
    IReadOnlyList<CollectionWarning> Warnings,
    CollectionRefusal? Refusal)
{
    /// <summary>A preview can be committed only when nothing refused it.</summary>
    public bool CanCommit => Refusal is null;

    // The synthesized equality would compare the warnings by reference; the list is part of the value.
    public bool Equals(CollectionPreview? other) =>
        other is not null &&
        Period == other.Period &&
        ReceivedOn == other.ReceivedOn &&
        ResolvedToday == other.ResolvedToday &&
        PeriodWasEarly == other.PeriodWasEarly &&
        PreviousAmountOwed == other.PreviousAmountOwed &&
        LedgerFigure == other.LedgerFigure &&
        ResultingAmountOwed == other.ResultingAmountOwed &&
        Equals(Locatario, other.Locatario) &&
        Equals(Propietario, other.Propietario) &&
        Warnings.SequenceEqual(other.Warnings) &&
        Refusal == other.Refusal;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Period);
        hash.Add(ReceivedOn);
        hash.Add(ResolvedToday);
        hash.Add(PeriodWasEarly);
        hash.Add(PreviousAmountOwed);
        hash.Add(LedgerFigure);
        hash.Add(ResultingAmountOwed);
        hash.Add(Locatario);
        hash.Add(Propietario);
        hash.Add(Refusal);

        foreach (var warning in Warnings)
        {
            hash.Add(warning);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// The pure half of collecting a tenant payment: from an account, a contract and a request it builds
/// the preview, and from a committable preview it lists the ledger movements the payment writes.
/// Neither appends anything; the desk does, inside its transaction.
///
/// The caller materialises the periods already due, in memory, before planning (design D3): this
/// planner works on the account it is given and an owed period is expected to be on it already.
/// </summary>
public static class CollectionPlanner
{
    public static CollectionPreview Plan(
        ContractAccount account,
        Contract contract,
        RecargoTerms terms,
        CollectionRequest request,
        DateOnly today,
        ReciboHeader header)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(header);

        if (request.AccountId != account.Id)
        {
            throw new ArgumentException("The request names another account.", nameof(request));
        }

        if (contract.Id != account.ContractId)
        {
            throw new ArgumentException("The contract is not the account's contract.", nameof(contract));
        }

        var receivedOn = request.ReceivedOn ?? today;

        CollectionPreview Refused(CollectionRefusal refusal, DateOnly? period = null, decimal previous = 0m) =>
            new(period, receivedOn, today, false, previous, 0m, previous, null, null, [], refusal);

        // The received-date rules come first: they say nothing about the account.
        if (receivedOn > today)
        {
            return Refused(CollectionRefusal.ReceivedOnInFuture);
        }

        if (today.DayNumber - receivedOn.DayNumber > CollectionRules.BackDateBoundDays)
        {
            return Refused(CollectionRefusal.ReceivedOnBeyondBound);
        }

        var previous = account.AmountOwedForPayment(receivedOn, terms);

        // Refused on what was typed, before truncation: -0.5 is a negative tasa.
        if (request.TasaMunicipal is < 0)
        {
            return Refused(CollectionRefusal.NegativeTasaMunicipal, previous: previous);
        }

        if (account.IsFrozen(receivedOn))
        {
            return Refused(CollectionRefusal.AccountFrozen, previous: previous);
        }

        DateOnly period;

        if (request.NamedPeriod is { } named)
        {
            var payability = account.PayabilityOf(named, receivedOn, contract);

            if (payability != PeriodPayability.Payable)
            {
                return Refused(RefusalFor(payability), named, previous);
            }

            period = named;
        }
        else if (account.DefaultPeriodToPay(receivedOn, contract) is { } chosen)
        {
            period = chosen;
        }
        else
        {
            return Refused(CollectionRefusal.NothingPayable, previous: previous);
        }

        var figures = FiguresFor(account, contract, terms, period, receivedOn);

        if (figures is null)
        {
            return Refused(CollectionRefusal.PeriodAlreadySettled, period, previous);
        }

        // What the account owes after the commit, on the same basis as the charge: the period as it
        // stands (its net) and the recargo measured to the received date leave, exactly what this
        // payment settles, because a paid period projects nothing afterwards.
        var resulting = previous - Math.Max(account.PeriodNet(period), 0m) - figures.Ledger.Recargo;

        CollectionPreview WithFigures(CollectionRefusal? refusal, ReciboDraftPair? drafts, CollectionWarning[] warnings) =>
            new(
                period, receivedOn, today, figures.Early, previous, figures.Total, resulting,
                drafts?.Locatario, drafts?.Propietario, warnings, refusal);

        // A rate never entered is not a rate of zero (spec Decision 22): no propietario recibo can be built.
        if (contract.HonorariosPercentage is not { } rate)
        {
            return WithFigures(CollectionRefusal.RateNeverEntered, null, []);
        }

        var drafts = ReciboBuilder.Build(
            figures.Ledger,
            new TypedReciboLines(request.TasaMunicipal, request.Seguro, request.OtrosConceptos),
            rate);

        CollectionWarning[] warnings = rate == 0m ? [CollectionWarning.ZeroAdministrationRate] : [];

        return request.HandedAmount != figures.Total
            ? WithFigures(CollectionRefusal.AmountNotTotal, drafts, warnings)
            : WithFigures(null, drafts, warnings);
    }

    /// <summary>
    /// The refusal that stops a commit, or null when it may go ahead (design D21). Any difference
    /// between the preview the operator saw and the one rebuilt from fresh data is a stale preview,
    /// including a period another operator has settled meanwhile; the fresh preview then says why.
    /// </summary>
    public static CollectionRefusal? Confirm(CollectionPreview shown, CollectionPreview fresh)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(fresh);

        return shown.Equals(fresh) ? shown.Refusal : CollectionRefusal.PreviewStale;
    }

    /// <summary>
    /// The movements a committable preview writes, in order: the period's rent and instalment when it
    /// is early and they are not on the account yet (design D1), the recargo when there is one, and the
    /// payment. Dated by the ledger date rule (D2). Offered, not appended; the desk appends them.
    ///
    /// An opening-balance month writes only its payment: it has no rent to bring and no recargo
    /// (spec Decision 31). A voided early payment paid again writes only its payment, because the rent
    /// and instalment of the first attempt are still on the account (spec Test 73).
    /// </summary>
    public static IReadOnlyList<AccountMovement> MovementsToWrite(
        ContractAccount account, Contract contract, CollectionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(preview);

        if (preview.Refusal is not null || preview.Period is null)
        {
            throw new InvalidOperationException("A refused preview writes nothing.");
        }

        var period = preview.Period.Value;
        var written = new List<AccountMovement>();

        var openingBalance = account.Movements.FirstOrDefault(m => m.Kind == MovementKind.OpeningBalance && m.Period == period);
        var accrual = account.Movements.FirstOrDefault(m => m.Kind == MovementKind.RentAccrual && m.Period == period);
        DateOnly chargeDate;

        if (openingBalance is not null)
        {
            chargeDate = openingBalance.OccurredOn;
        }
        else if (accrual is not null)
        {
            chargeDate = accrual.OccurredOn;
        }
        else
        {
            var begun = AccrualSchedule.PeriodsBegun(contract, preview.ReceivedOn).Single(p => p.Period == period);
            chargeDate = begun.DueDate;
            written.Add(AccountMovement.RentAccrual(Guid.NewGuid(), begun.Canon, begun.Period, begun.DueDate));
        }

        if (preview.PeriodWasEarly &&
            openingBalance is null &&
            !account.HasInstalmentFor(period) &&
            AccrualSchedule.InstalmentAmountFor(contract, period) is { } fee)
        {
            written.Add(AccountMovement.ContractualFeeInstalment(Guid.NewGuid(), fee, period, chargeDate));
        }

        var latestCorrection = account.Movements
            .Where(m => m.Kind == MovementKind.Correction && m.Period == period)
            .Select(m => (DateOnly?)m.OccurredOn)
            .Max();
        var paymentDate = LedgerDating.PaymentDate(preview.ReceivedOn, chargeDate, latestCorrection);

        if (preview.Locatario!.Lines.SingleOrDefault(l => l.Kind == ReciboLineKind.Recargo) is { } recargo)
        {
            written.Add(AccountMovement.RecargoCharged(Guid.NewGuid(), recargo.Amount, period, paymentDate));
        }

        written.Add(AccountMovement.Payment(Guid.NewGuid(), -preview.LedgerFigure, period, paymentDate));

        return written;
    }

    private static CollectionRefusal RefusalFor(PeriodPayability payability) => payability switch
    {
        PeriodPayability.AlreadySettled => CollectionRefusal.PeriodAlreadySettled,
        PeriodPayability.NotBegun => CollectionRefusal.PeriodNotBegun,
        PeriodPayability.NotOnContract => CollectionRefusal.PeriodNotOnContract,
        PeriodPayability.AccountFrozen => CollectionRefusal.AccountFrozen,
        _ => throw new ArgumentOutOfRangeException(nameof(payability), payability, "A payable period has no refusal."),
    };

    /// <summary>The figures of one period as the payment will settle them.</summary>
    private sealed record PeriodFigures(bool Early, ReciboLedgerFigures Ledger, decimal Total);

    /// <summary>
    /// What paying <paramref name="period"/> as of <paramref name="receivedOn"/> settles, or null when
    /// there is nothing to settle. Everything the period holds dated up to the payment's ledger date,
    /// plus the rent and instalment an early payment is about to bring onto the account.
    /// </summary>
    private static PeriodFigures? FiguresFor(
        ContractAccount account, Contract contract, RecargoTerms terms, DateOnly period, DateOnly receivedOn)
    {
        var openingBalance = account.Movements.FirstOrDefault(m => m.Kind == MovementKind.OpeningBalance && m.Period == period);

        if (openingBalance is not null)
        {
            var figure = account.PeriodNet(period);

            return figure > 0m
                ? new PeriodFigures(false, ReciboLedgerFigures.ForOpeningBalance(figure), figure)
                : null;
        }

        var accrual = account.Movements.FirstOrDefault(m => m.Kind == MovementKind.RentAccrual && m.Period == period);
        var begun = AccrualSchedule.PeriodsBegun(contract, receivedOn).FirstOrDefault(p => p.Period == period);

        if ((accrual?.OccurredOn ?? begun?.DueDate) is not { } dueDate)
        {
            return null;
        }

        var early = dueDate > receivedOn;

        // A due period that is not on the account was not materialised: nothing here to settle.
        if (accrual is null && !early)
        {
            return null;
        }

        var heldInstalment = account.Movements
            .Where(m => m.Kind == MovementKind.ContractualFeeInstalment && m.Period == period)
            .Sum(m => m.Amount);
        var newInstalment = early && !account.HasInstalmentFor(period)
            ? AccrualSchedule.InstalmentAmountFor(contract, period) ?? 0m
            : 0m;
        var newRent = accrual is null ? begun!.Canon : 0m;

        var rent = account.PeriodNet(period) - heldInstalment + newRent;
        var instalment = heldInstalment + newInstalment;

        if (rent <= 0m)
        {
            return null;
        }

        // Charged on the recargo projected as of the received date, truncated to a whole peso (spec Test 8).
        var recargo = decimal.Truncate(account.RecargoProjectedForPayment(period, receivedOn, terms));

        return new PeriodFigures(
            early,
            ReciboLedgerFigures.ForRentPeriod(rent, instalment > 0m ? instalment : null, recargo),
            rent + instalment + recargo);
    }
}
