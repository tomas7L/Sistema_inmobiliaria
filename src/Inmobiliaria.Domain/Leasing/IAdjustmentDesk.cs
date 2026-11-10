using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// What the Ajustes screen does (gestion-de-alquileres spec Requirements 14 and 15, design §3.4): open the summary of a
/// contract's due adjustment, confirm exactly what that summary showed, and list the adjustments already confirmed.
/// Anything an operator can cause comes back as an <see cref="AdjustmentRefusal"/>.
/// </summary>
public interface IAdjustmentDesk
{
    /// <summary>
    /// The summary of the oldest adjustment the contract has due, priced from its current canon. Nothing is written.
    /// </summary>
    Task<AdjustmentPreviewResult> PreviewAsync(Guid contractId, CancellationToken ct = default);

    /// <summary>
    /// Writes the adjustment and the new canon in one transaction, recording who confirmed. The summary is rebuilt under
    /// the account's and the contract's locks and must match <paramref name="preview"/>, or nothing is written.
    /// </summary>
    Task<AdjustmentConfirmResult> ConfirmAsync(AdjustmentPreview preview, CancellationToken ct = default);

    /// <summary>The contract's confirmed adjustments, the most recently confirmed first.</summary>
    Task<IReadOnlyList<AdjustmentHistoryEntry>> HistoryAsync(Guid contractId, CancellationToken ct = default);
}

/// <param name="IndexName">The index as the catalog names it.</param>
/// <param name="BasePeriod">The month the window starts from.</param>
/// <param name="BaseLevel">That month's level.</param>
/// <param name="EndPeriod">The month the window ends at.</param>
/// <param name="EndLevel">That month's level.</param>
/// <param name="Variation">The index's own variation, in percent.</param>
public sealed record AdjustmentIndexLine(
    string IndexName, IndexPeriod BasePeriod, decimal BaseLevel, IndexPeriod EndPeriod, decimal EndLevel, decimal Variation);

/// <summary>
/// The summary an operator confirms (spec Requirement 14). A class, since a record's equality would not compare the
/// lines; <see cref="SameFiguresAs"/> is what decides whether a confirmation still matches.
/// </summary>
public sealed class AdjustmentPreview(
    Guid contractId,
    string? tenantName,
    DateOnly dueDate,
    decimal previousCanon,
    IReadOnlyList<AdjustmentIndexLine> indices,
    CombinationRule combination,
    decimal coefficient,
    decimal untruncatedNewCanon,
    decimal newCanon,
    DateOnly appliesFrom)
{
    public Guid ContractId { get; } = contractId;

    public string? TenantName { get; } = tenantName;

    /// <summary>The adjustment due date, which keeps driving the calendar.</summary>
    public DateOnly DueDate { get; } = dueDate;

    public decimal PreviousCanon { get; } = previousCanon;

    public IReadOnlyList<AdjustmentIndexLine> Indices { get; } = indices;

    public CombinationRule Combination { get; } = combination;

    /// <summary>The variation applied, in percent.</summary>
    public decimal Coefficient { get; } = coefficient;

    /// <summary>Before the whole-peso rule, so the operator sees what is dropped.</summary>
    public decimal UntruncatedNewCanon { get; } = untruncatedNewCanon;

    public decimal NewCanon { get; } = newCanon;

    /// <summary>The first period the new canon prices.</summary>
    public DateOnly AppliesFrom { get; } = appliesFrom;

    /// <summary>It prices from a later month than the one it fell due in.</summary>
    public bool IsLate => AdjustmentTiming.IsLate(DueDate, AppliesFrom);

    /// <summary>Every figure the operator saw: the canon, the window, each level, the result and the period it applies from.</summary>
    public bool SameFiguresAs(AdjustmentPreview other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return ContractId == other.ContractId
            && DueDate == other.DueDate
            && PreviousCanon == other.PreviousCanon
            && Combination == other.Combination
            && Coefficient == other.Coefficient
            && NewCanon == other.NewCanon
            && AppliesFrom == other.AppliesFrom
            && Indices.SequenceEqual(other.Indices);
    }
}

/// <param name="Preview">The summary, when there is one to confirm.</param>
/// <param name="Refusal">Why there is none.</param>
/// <param name="Missing">With <see cref="AdjustmentRefusal.IndexValuesMissing"/>, each index and month still unpublished.</param>
public sealed record AdjustmentPreviewResult(AdjustmentPreview? Preview, AdjustmentRefusal? Refusal, IReadOnlyList<MissingIndexValue> Missing)
{
    public static AdjustmentPreviewResult Ready(AdjustmentPreview preview) => new(preview, null, []);

    public static AdjustmentPreviewResult Refused(AdjustmentRefusal refusal, IReadOnlyList<MissingIndexValue>? missing = null) =>
        new(null, refusal, missing ?? []);
}

/// <param name="Confirmed">The adjustment as written, when it was.</param>
/// <param name="Refusal">Why nothing was written.</param>
public sealed record AdjustmentConfirmResult(AdjustmentHistoryEntry? Confirmed, AdjustmentRefusal? Refusal)
{
    public bool Succeeded => Confirmed is not null;

    public static AdjustmentConfirmResult Done(AdjustmentHistoryEntry confirmed) => new(confirmed, null);

    public static AdjustmentConfirmResult Refused(AdjustmentRefusal refusal) => new(null, refusal);
}

/// <param name="Id">The confirmed adjustment.</param>
/// <param name="DueDate">Its adjustment due date.</param>
/// <param name="AppliesFrom">The first period it prices; null on rows written before this was recorded, which price from their due date.</param>
/// <param name="PreviousCanon">The canon it replaced.</param>
/// <param name="NewCanon">The canon it set.</param>
/// <param name="Coefficient">The variation applied, in percent.</param>
/// <param name="Kind">Regular, or a correction of an earlier one.</param>
/// <param name="ConfirmedAt">When it was confirmed.</param>
/// <param name="ConfirmedBy">Who confirmed it, or null on rows written before that was recorded.</param>
/// <param name="Indices">Each index's base and end level, as it was used.</param>
public sealed record AdjustmentHistoryEntry(
    Guid Id,
    DateOnly DueDate,
    DateOnly? AppliesFrom,
    decimal PreviousCanon,
    decimal NewCanon,
    decimal Coefficient,
    AdjustmentKind Kind,
    DateTimeOffset ConfirmedAt,
    string? ConfirmedBy,
    IReadOnlyList<AdjustmentIndexLine> Indices);

public enum AdjustmentRefusal
{
    /// <summary>The contract is no longer there.</summary>
    ContractNotFound,

    /// <summary>The contract has no adjustment due today: none yet, it has ended, or it has no clause.</summary>
    NotDue,

    /// <summary>An index has no published value for a month the window needs.</summary>
    IndexValuesMissing,

    /// <summary>This adjustment was already confirmed, by someone else or a moment ago.</summary>
    AlreadyConfirmed,

    /// <summary>A figure changed since the summary was opened; it must be opened again before confirming.</summary>
    PreviewStale,
}

public static class AdjustmentRefusalText
{
    public static string OperatorText(this AdjustmentRefusal refusal) => refusal switch
    {
        AdjustmentRefusal.ContractNotFound => "El contrato ya no existe. Actualice la lista.",
        AdjustmentRefusal.NotDue => "Este contrato no tiene un ajuste vencido para confirmar. Actualice la lista.",
        AdjustmentRefusal.IndexValuesMissing => "Faltan valores de índice para calcular este ajuste.",
        AdjustmentRefusal.AlreadyConfirmed => "Este ajuste ya fue confirmado. La lista se actualizó.",
        AdjustmentRefusal.PreviewStale => "Algo cambió desde que abrió el resumen. Revise el resumen actualizado antes de confirmar.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };
}
