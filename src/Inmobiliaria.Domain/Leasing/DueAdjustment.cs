using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// One index a due adjustment still needs a published value for, and the exact period it needs
/// it in (spec "The Pending Worklist Is Reminder-Ready"). This is what lets a reminder say
/// "3 contracts are waiting for August's IPC" instead of firing a blind monthly alert.
/// </summary>
public sealed record MissingIndexValue(Guid EconomicIndexId, string IndexName, IndexPeriod Period);

/// <summary>
/// One contract whose derived next adjustment due date has arrived (design Decision 8). Query
/// starts FROM <c>adjustment_clauses</c>, never from <c>contracts</c>, so a fixed-price contract
/// with no clause is absent by the query's own construction, not by a filter (design Decision
/// 5). Only <see cref="ContractStatus.Active"/> contracts are ever produced — an ended lease has
/// no canon left to adjust.
/// </summary>
/// <param name="Missing">
/// Empty exactly when every referenced index resolved to a published value for both
/// <paramref name="BasePeriod"/> and <paramref name="EndPeriod"/>; otherwise names each index
/// and period still outstanding. Never invents a value and never averages a partial set (spec
/// "Missing Index Value Leaves the Adjustment Pending").
/// </param>
/// <param name="Coefficient">Null exactly when <paramref name="Missing"/> is non-empty.</param>
/// <param name="ProposedCanon">Null exactly when <paramref name="Missing"/> is non-empty.</param>
public sealed record DueAdjustment(
    Guid ContractId,
    DateOnly DueDate,
    decimal CurrentCanon,
    IndexPeriod BasePeriod,
    IndexPeriod EndPeriod,
    IReadOnlyList<MissingIndexValue> Missing,
    decimal? Coefficient,
    decimal? ProposedCanon);
