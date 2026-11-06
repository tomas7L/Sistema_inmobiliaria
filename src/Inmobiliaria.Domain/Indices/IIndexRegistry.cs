namespace Inmobiliaria.Domain.Indices;

/// <summary>
/// What the Índices screen does (gestion-de-alquileres spec Requirements 11 and 12, design §4): list the catalog with its
/// values, type a published level, correct one, see the adjustments a correction affects and the record corrections leave.
/// The catalog itself is seeded, so nothing here creates an index. Anything an operator can cause comes back as an
/// <see cref="IndexRefusal"/>.
/// </summary>
public interface IIndexRegistry
{
    /// <summary>Every index by name, each with its values, the latest month first.</summary>
    Task<IReadOnlyList<IndexListing>> ListAsync(CancellationToken ct = default);

    /// <param name="indexId">The index.</param>
    /// <param name="period">The month the level was published for.</param>
    /// <param name="level">The published level.</param>
    /// <param name="acceptFarLevel">The operator saw the warning for a level far from the nearest earlier one, and confirms.</param>
    /// <param name="ct">Cancels the write.</param>
    Task<IndexEntryResult> AddAsync(Guid indexId, IndexPeriod period, decimal level, bool acceptFarLevel, CancellationToken ct = default);

    /// <summary>
    /// Corrects a stored level. The database records who, when, the previous and the new level by itself (spec Decision 42).
    /// Saving the level already stored writes nothing and records nothing.
    /// </summary>
    /// <param name="valueId">The stored value.</param>
    /// <param name="newLevel">The corrected level.</param>
    /// <param name="acceptFarLevel">As in <see cref="AddAsync"/>.</param>
    /// <param name="ct">Cancels the write.</param>
    Task<IndexEntryResult> CorrectAsync(Guid valueId, decimal newLevel, bool acceptFarLevel, CancellationToken ct = default);

    /// <summary>
    /// The confirmed adjustments whose own copy of this index and month differs from <paramref name="newLevel"/> (spec
    /// Requirement 18), the most recent first. It changes nothing: an adjustment keeps its copy until someone corrects it.
    /// </summary>
    /// <param name="valueId">The stored value.</param>
    /// <param name="newLevel">The level to compare each copy with: the one being typed, or the one just saved.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<IReadOnlyList<AffectedAdjustment>> ListAffectedAdjustmentsAsync(Guid valueId, decimal newLevel, CancellationToken ct = default);

    /// <summary>The record of every change to this value's level, the most recent first (spec Decision 42).</summary>
    Task<IReadOnlyList<IndexCorrectionRecord>> ListCorrectionsAsync(Guid valueId, CancellationToken ct = default);
}

/// <summary>One index and its values. A class, since a record's equality would not compare the list.</summary>
public sealed class IndexListing(Guid id, string name, IndexPeriod? discontinuedFrom, IReadOnlyList<IndexValueListing> values)
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    public IndexPeriod? DiscontinuedFrom { get; } = discontinuedFrom;

    public IReadOnlyList<IndexValueListing> Values { get; } = values;
}

/// <param name="Id">The stored value.</param>
/// <param name="Period">The month it was published for.</param>
/// <param name="Level">The published level.</param>
public sealed record IndexValueListing(Guid Id, IndexPeriod Period, decimal Level);

/// <param name="AdjustmentId">The confirmed adjustment.</param>
/// <param name="ContractId">Its contract.</param>
/// <param name="TenantName">The tenant's full name, or null when the contract has none.</param>
/// <param name="EffectiveDate">The adjustment's due date.</param>
/// <param name="UsedLevel">The level the adjustment's own copy holds for this index and month.</param>
public sealed record AffectedAdjustment(Guid AdjustmentId, Guid ContractId, string? TenantName, DateOnly EffectiveDate, decimal UsedLevel);

/// <param name="PreviousLevel">The level that was overwritten.</param>
/// <param name="NewLevel">The level that replaced it.</param>
/// <param name="RecordedBy">The database's record of who did it.</param>
/// <param name="RecordedAt">The database's record of when, in the agency's zone.</param>
public sealed record IndexCorrectionRecord(decimal PreviousLevel, decimal NewLevel, string RecordedBy, DateTimeOffset RecordedAt);

public enum IndexRefusal
{
    /// <summary>The index being typed for is no longer there.</summary>
    IndexNotFound,

    /// <summary>The value being corrected is no longer there.</summary>
    ValueNotFound,

    /// <summary>The period is a month that has not begun.</summary>
    FuturePeriod,

    /// <summary>The level is zero or negative.</summary>
    LevelNotPositive,

    /// <summary>The level has more decimals than <c>numeric(18,6)</c> keeps.</summary>
    TooManyDecimals,

    /// <summary>The level has more whole digits than <c>numeric(18,6)</c> keeps.</summary>
    LevelTooLarge,

    /// <summary>The index already has a value for this month. The operator is offered to correct it.</summary>
    DuplicatePeriod,

    /// <summary>The level is far from the nearest earlier one and has not been confirmed yet. A warning, never a final refusal.</summary>
    FarLevelNotAccepted,
}

/// <param name="Refusal">Why nothing was written, or null on success.</param>
/// <param name="Value">The value as stored, on success.</param>
/// <param name="PreviousLevel">The level before a correction, on success.</param>
/// <param name="Existing">The value already there on <see cref="IndexRefusal.DuplicatePeriod"/>, or the nearest earlier one on <see cref="IndexRefusal.FarLevelNotAccepted"/>.</param>
public sealed record IndexEntryResult(IndexRefusal? Refusal, IndexValueListing? Value, decimal? PreviousLevel, IndexValueListing? Existing)
{
    public bool Succeeded => Refusal is null;

    public static IndexEntryResult Saved(IndexValueListing value, decimal? previousLevel = null) => new(null, value, previousLevel, null);

    public static IndexEntryResult Refused(IndexRefusal refusal, IndexValueListing? existing = null) => new(refusal, null, null, existing);
}

public static class IndexRefusalText
{
    public static string OperatorText(this IndexRefusal refusal) => refusal switch
    {
        IndexRefusal.IndexNotFound => "El índice ya no existe. Actualice la lista.",
        IndexRefusal.ValueNotFound => "El valor ya no existe. Actualice la lista.",
        IndexRefusal.FuturePeriod => "El período no puede ser un mes futuro.",
        IndexRefusal.LevelNotPositive => "El nivel tiene que ser mayor que cero.",
        IndexRefusal.TooManyDecimals => "El nivel admite hasta seis decimales.",
        IndexRefusal.LevelTooLarge => "El nivel es demasiado grande. Revise el número.",
        IndexRefusal.DuplicatePeriod => "Este índice ya tiene un valor para ese período. Puede corregirlo.",
        IndexRefusal.FarLevelNotAccepted => "El nivel está muy lejos del anterior. Revíselo y confirme si es correcto.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal without operator text."),
    };
}
