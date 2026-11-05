using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// What the contract screens do with contracts (gestion-de-alquileres spec Requirements 6 to 8, design §4): list them, read
/// one, create one with its account, and suggest a rate. Anything an operator can cause comes back as a
/// <see cref="ContractRefusal"/> or a warning, never as an exception.
/// </summary>
public interface IContractRegistry
{
    /// <summary>Contracts matching <paramref name="text"/> by tenant, lessor or unit, in <paramref name="status"/> when given; blank text lists them all.</summary>
    Task<IReadOnlyList<ContractListing>> ListAsync(string? text, ContractStatus? status, CancellationToken ct = default);

    /// <summary>One contract in full, or null when it is not there.</summary>
    Task<ContractDetail?> DetailAsync(Guid contractId, CancellationToken ct = default);

    /// <summary>Writes the contract, its parties, shares, optional clause and empty account in one transaction, or nothing.</summary>
    /// <param name="draft">The form.</param>
    /// <param name="acceptWarnings">The operator saw the warnings and wants to proceed. Never overrides a refusal.</param>
    /// <param name="ct">Cancels the write.</param>
    Task<ContractResult> CreateAsync(ContractDraft draft, bool acceptWarnings, CancellationToken ct = default);

    /// <summary>The rate of this lessor's contract with the latest start date that carries one, or null (spec Requirement 7).</summary>
    Task<decimal?> SuggestRateAsync(Guid lessorId, CancellationToken ct = default);
}

/// <param name="Id">The contract.</param>
/// <param name="Tenants">Names as the desk shows them.</param>
/// <param name="Lessors">Names as the desk shows them.</param>
/// <param name="Units">Addresses as the desk shows them.</param>
/// <param name="Status">Its state. A contract past its end stays <c>Active</c> until someone ends it.</param>
/// <param name="StartDate">When it starts.</param>
/// <param name="NominalEndDate">When it says it ends.</param>
/// <param name="IsPastEnd">Not ended and past its nominal end date: flagged, never ended by this.</param>
public sealed record ContractListing(
    Guid Id,
    string Tenants,
    string Lessors,
    string Units,
    ContractStatus Status,
    DateOnly StartDate,
    DateOnly NominalEndDate,
    bool IsPastEnd);

public sealed record ContractPartyLine(Guid PartyId, string Name, PartyRole Role);

public sealed record ContractUnitLine(Guid UnitId, string Address, decimal Percentage);

public sealed record ContractClauseLine(IReadOnlyList<string> IndexNames, int IntervalMonths, RoundingRule RoundingRule);

public sealed record ContractAdjustmentLine(DateOnly EffectiveDate, decimal PreviousCanon, decimal NewCanon, decimal Coefficient, AdjustmentKind Kind);

public sealed record ContractDetail(
    Guid Id,
    ContractStatus Status,
    DateOnly StartDate,
    DateOnly NominalEndDate,
    bool IsPastEnd,
    DateOnly? NoticeGivenDate,
    DateOnly? PlannedMoveOutDate,
    DateOnly? ActualEndDate,
    EndReason? EndReason,
    DateOnly? CutoverDate,
    decimal MonthlyRent,
    int DueDay,
    decimal? HonorariosPercentage,
    ContractualFeePlan? FeePlan,
    IReadOnlyList<ContractPartyLine> Parties,
    IReadOnlyList<ContractUnitLine> Units,
    ContractClauseLine? Clause,
    IReadOnlyList<ContractAdjustmentLine> Adjustments);

public enum ContractRefusal
{
    TenantMissing,
    LessorMissing,
    UnitMissing,
    DatesMissing,
    StartNotOnTheFirst,
    EndBeforeStart,

    /// <summary>A start before the current month carries a cutover date, which only an Admin can load (spec Decision 31).</summary>
    StartBeforeCurrentMonth,
    CutoverAfterToday,
    CutoverBeforeStart,

    /// <summary>The database refused the cutover date of someone who is not an Admin (spec Requirement 21).</summary>
    CutoverAdminOnly,
    RentNotWholePositive,
    DueDayOutOfRange,
    HonorariosOutOfRange,
    FeePlanInvalid,
    SharesNotHundred,
    ClauseInvalid,

    /// <summary>A unit is under an <c>Active</c> contract (spec Decision 30).</summary>
    UnitUnderActiveContract,
    PartyNotFound,
    UnitNotFound,
    IndexNotFound,
}

public enum ContractWarningKind
{
    /// <summary>No rate entered: the desk refuses collection until there is one.</summary>
    NoHonorarios,

    /// <summary>The start is this month and its first due date has passed.</summary>
    FirstDueDatePassed,

    /// <summary>A unit is under a <c>PendingTermination</c> contract, with its move-out date.</summary>
    UnitBeingVacated,
}

/// <param name="Kind">What it warns about.</param>
/// <param name="Date">The month overdue, or the move-out date.</param>
/// <param name="Unit">The unit being vacated.</param>
public sealed record ContractWarning(ContractWarningKind Kind, DateOnly? Date = null, string? Unit = null);

/// <param name="Refusal">Why nothing was written, or null.</param>
/// <param name="Contract">The contract written, on success.</param>
/// <param name="Warnings">What has to be accepted first; nothing was written while this holds any.</param>
/// <param name="CurrentTenant">Who rents the unit, on <see cref="ContractRefusal.UnitUnderActiveContract"/>.</param>
public sealed record ContractResult(ContractRefusal? Refusal, ContractListing? Contract, IReadOnlyList<ContractWarning> Warnings, string? CurrentTenant)
{
    public bool Succeeded => Contract is not null;

    public static ContractResult Saved(ContractListing contract) => new(null, contract, [], null);

    public static ContractResult Refused(ContractRefusal refusal, string? currentTenant = null) => new(refusal, null, [], currentTenant);

    public static ContractResult NeedsAccepting(IReadOnlyList<ContractWarning> warnings) => new(null, null, warnings, null);
}

public static class ContractRefusalText
{
    private static readonly string[] Months =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    /// <param name="refusal">The refusal.</param>
    /// <param name="currentTenant">Who rents the unit, named by <see cref="ContractRefusal.UnitUnderActiveContract"/>.</param>
    public static string OperatorText(this ContractRefusal refusal, string? currentTenant = null) => refusal switch
    {
        ContractRefusal.TenantMissing => "Elija al inquilino del contrato.",
        ContractRefusal.LessorMissing => "Elija al menos un propietario.",
        ContractRefusal.UnitMissing => "Elija al menos una unidad.",
        ContractRefusal.DatesMissing => "Escriba la fecha de inicio y la de fin.",
        ContractRefusal.StartNotOnTheFirst => "Un contrato empieza a regir el día 1 del mes.",
        ContractRefusal.EndBeforeStart => "La fecha de fin no puede ser anterior a la de inicio.",
        ContractRefusal.StartBeforeCurrentMonth => "Un contrato anterior al mes actual lleva fecha de corte, y tiene que cargarlo un Admin.",
        ContractRefusal.CutoverAfterToday => "La fecha de corte no puede ser posterior a hoy.",
        ContractRefusal.CutoverBeforeStart => "La fecha de corte no puede ser anterior al inicio del contrato.",
        ContractRefusal.CutoverAdminOnly => "Solo un Admin puede cargar un contrato con fecha de corte.",
        ContractRefusal.RentNotWholePositive => "El alquiler tiene que ser un monto en pesos enteros, mayor que cero.",
        ContractRefusal.DueDayOutOfRange => "El día de vencimiento tiene que estar entre 1 y 31.",
        ContractRefusal.HonorariosOutOfRange => "Los honorarios tienen que estar entre 0 y 100, con hasta dos decimales.",
        ContractRefusal.FeePlanInvalid => "El plan de honorarios lleva un monto mayor que cero y de 1 a 6 cuotas.",
        ContractRefusal.SharesNotHundred => "Las partes de las unidades tienen que sumar exactamente el 100% del alquiler.",
        ContractRefusal.ClauseInvalid => "La cláusula de ajuste lleva al menos un índice, sin repetir, y un intervalo de 1 a 60 meses.",
        ContractRefusal.UnitUnderActiveContract => $"Esa unidad ya está alquilada a {currentTenant ?? "otra persona"}.",
        ContractRefusal.PartyNotFound => "Una de las personas elegidas ya no existe. Actualice la lista.",
        ContractRefusal.UnitNotFound => "Una de las unidades elegidas ya no existe. Actualice la lista.",
        ContractRefusal.IndexNotFound => "Uno de los índices elegidos ya no existe. Actualice la lista.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };

    public static string OperatorText(this ContractWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        return warning.Kind switch
        {
            ContractWarningKind.NoHonorarios =>
                "Sin porcentaje de honorarios, la caja no va a poder cobrar este contrato hasta que se cargue.",
            ContractWarningKind.FirstDueDatePassed =>
                $"El primer vencimiento ya pasó: {Months[warning.Date!.Value.Month - 1]} queda vencido.",
            ContractWarningKind.UnitBeingVacated =>
                $"{warning.Unit} se desocupa el {warning.Date!.Value:dd/MM/yyyy}.",
            _ => throw new ArgumentOutOfRangeException(nameof(warning), warning.Kind, "A warning without operator text."),
        };
    }
}
