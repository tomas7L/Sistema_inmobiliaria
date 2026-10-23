namespace Inmobiliaria.Domain.GoLive;

/// <summary>
/// The two go-live acts only an Admin performs (ui-de-cobranza spec Requirements 17 to 19, design
/// section 5.3): a pre-existing contract's cutover date, and the number the first recibo will carry.
/// Refusals are values (design D25): the domain's checks and the database's named errors both come
/// back as a <see cref="SetupRefusal"/>; anything else surfaces as an error.
/// </summary>
public interface IGoLiveSetup
{
    /// <summary>Every contract, whatever its status, with its cutover date and whether it can still change.</summary>
    Task<IReadOnlyList<CutoverListing>> ListContractsAsync(CancellationToken ct = default);

    /// <summary>Sets or corrects a contract's cutover date through <c>Contract.SetCutoverDate</c>.</summary>
    Task<SetupResult> SetCutoverDateAsync(Guid contractId, DateOnly cutoverDate, CancellationToken ct = default);

    /// <summary>The recibo starting number and whether a recibo exists yet. Readable by both roles.</summary>
    Task<StartingNumberState> GetStartingNumberStateAsync(CancellationToken ct = default);

    /// <summary>Sets or corrects the recibo starting number while no recibo exists.</summary>
    Task<SetupResult> SetStartingNumberAsync(int startingNumber, CancellationToken ct = default);
}

/// <summary>Whether a contract's cutover date can still be set.</summary>
public enum CutoverState
{
    /// <summary>The account holds no movement.</summary>
    Changeable,

    /// <summary>The account holds a movement, so the date is one-way.</summary>
    Locked,

    /// <summary>The contract has no account, so the date cannot be set at all (spec Decision 30).</summary>
    NoAccount,
}

/// <summary>One line of the go-live list.</summary>
/// <param name="ContractId">The contract.</param>
/// <param name="Tenants">The tenant's name, "Last, First".</param>
/// <param name="Units">The contract's units, described as the recibo describes them.</param>
/// <param name="StartDate">The contract's start date, the earliest cutover date allowed.</param>
/// <param name="CutoverDate">The stored cutover date, or null when there is none.</param>
/// <param name="State">Whether the date can still be set.</param>
public sealed record CutoverListing(
    Guid ContractId, string Tenants, string Units, DateOnly StartDate, DateOnly? CutoverDate, CutoverState State);

/// <summary>The agency's recibo starting number (spec Requirement 19).</summary>
/// <param name="StartingNumber">The stored number, or null when it was never set.</param>
/// <param name="AnyReciboIssued">True once a recibo exists, which locks the number.</param>
public sealed record StartingNumberState(int? StartingNumber, bool AnyReciboIssued)
{
    public bool IsSet => StartingNumber is not null;

    public bool IsLocked => AnyReciboIssued;
}

/// <summary>Why a go-live setting was refused.</summary>
public enum SetupRefusal
{
    CutoverAfterToday,
    CutoverBeforeContractStart,
    CutoverLocked,
    CutoverAdminOnly,
    NoAccount,
    StartingNumberLocked,
    StartingNumberAdminOnly,
    StartingNumberOutOfRange,
}

/// <summary>A setup call's answer: success, or the refusal as a value.</summary>
public sealed record SetupResult(SetupRefusal? Refusal)
{
    public static readonly SetupResult Success = new((SetupRefusal?)null);

    public bool Succeeded => Refusal is null;

    public static SetupResult Refused(SetupRefusal refusal) => new(refusal);
}

/// <summary>The operator-facing text of each setup refusal. Spanish, because the operator reads it.</summary>
public static class SetupRefusalText
{
    public static string OperatorText(this SetupRefusal refusal) => refusal switch
    {
        SetupRefusal.CutoverAfterToday =>
            "La fecha de corte no puede ser posterior a hoy.",
        SetupRefusal.CutoverBeforeContractStart =>
            "La fecha de corte no puede ser anterior al inicio del contrato.",
        SetupRefusal.CutoverLocked =>
            "La fecha de corte ya no se puede cambiar: la cuenta del contrato tiene movimientos.",
        SetupRefusal.CutoverAdminOnly =>
            "Sólo un Administrador puede cambiar la fecha de corte.",
        SetupRefusal.NoAccount =>
            "No se puede cargar: el contrato no tiene cuenta.",
        SetupRefusal.StartingNumberLocked =>
            "El número inicial de recibo ya no se puede cambiar: ya se emitió un recibo.",
        SetupRefusal.StartingNumberAdminOnly =>
            "Sólo un Administrador puede cargar el número inicial de recibo.",
        SetupRefusal.StartingNumberOutOfRange =>
            "El número inicial de recibo debe ser un número entero mayor que cero.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };
}
