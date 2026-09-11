namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// A candidate rent share for one unit, expressed as a percentage. Immutable value
/// object; validated only when applied via <see cref="Contract.SetUnitShares"/>.
/// </summary>
public readonly record struct UnitShare(Guid UnitId, decimal Percentage);
