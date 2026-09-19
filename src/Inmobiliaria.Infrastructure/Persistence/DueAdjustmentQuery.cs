using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="IDueAdjustmentQuery"/> (design Decision 8). Starts
/// FROM <c>adjustment_clauses</c>, joined to <c>contracts</c> filtered to
/// <see cref="ContractStatus.Active"/> — a fixed-price contract with no clause is absent by the
/// query's own construction, and an ended lease has no canon left to adjust. Due-date derivation,
/// successor resolution and the coefficient are all pure domain logic
/// (<see cref="AdjustmentSchedule"/>, <see cref="IndexResolver"/>, <see cref="AdjustmentMath"/>);
/// this class only fetches the rows those functions need and applies them in memory.
/// </summary>
public sealed class DueAdjustmentQuery : IDueAdjustmentQuery
{
    private readonly InmobiliariaDbContext _context;

    public DueAdjustmentQuery(InmobiliariaDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<DueAdjustment>> GetDueAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var clauseRows = await _context.AdjustmentClauses
            .AsNoTracking()
            .Include(clause => clause.Indices)
            .Join(
                _context.Contracts.AsNoTracking().Where(c => c.Status == ContractStatus.Active),
                clause => clause.ContractId,
                contract => contract.Id,
                (clause, contract) => new { Clause = clause, contract.Id, contract.StartDate, contract.MonthlyRent })
            .ToListAsync(ct);

        if (clauseRows.Count == 0)
        {
            return [];
        }

        var contractIds = clauseRows.Select(r => r.Id).ToArray();

        var lastEffectiveDates = await _context.RentAdjustments
            .AsNoTracking()
            .Where(a => contractIds.Contains(a.ContractId))
            .GroupBy(a => a.ContractId)
            .Select(g => new { ContractId = g.Key, LastEffectiveDate = g.Max(a => a.EffectiveDate) })
            .ToDictionaryAsync(x => x.ContractId, x => (DateOnly?)x.LastEffectiveDate, ct);

        var catalog = await _context.EconomicIndices.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var levels = await _context.IndexValues
            .AsNoTracking()
            .ToDictionaryAsync(v => (v.EconomicIndexId, v.Period), v => v.Level, ct);

        var results = new List<DueAdjustment>();

        foreach (var row in clauseRows)
        {
            var lastEffectiveDate = lastEffectiveDates.GetValueOrDefault(row.Id);
            var dueDate = AdjustmentSchedule.NextDueDate(row.StartDate, row.Clause.IntervalMonths, lastEffectiveDate);

            if (dueDate > asOf)
            {
                continue;
            }

            // Design Decision 8's window: for due date D and interval N, the adjustment covers
            // the N months D-N .. D-1 — the end period is the last of those, the base period is
            // the one preceding the window.
            var endPeriod = new IndexPeriod(dueDate.Year, dueDate.Month).AddMonths(-1);
            var basePeriod = endPeriod.AddMonths(-row.Clause.IntervalMonths);

            var (inputs, missing) = ResolveIndices(row.Clause, catalog, levels, basePeriod, endPeriod);

            decimal? coefficient = null;
            decimal? proposedCanon = null;

            if (missing.Count == 0)
            {
                var mathResult = AdjustmentMath.Compute(row.Clause.Combination, inputs);

                if (mathResult.Coefficient is { } computedCoefficient)
                {
                    coefficient = computedCoefficient;
                    var proposal = new AdjustmentProposal(
                        row.MonthlyRent, mathResult.IndexValues, row.Clause.Combination, computedCoefficient, dueDate);
                    proposedCanon = proposal.TruncatedNewCanon;
                }
                else
                {
                    // Defensive fallback only: every resolution, splice and level gap this
                    // adapter knows about is already screened in ResolveIndices, so AdjustmentMath
                    // should never itself return pending here. If a future reason does, the
                    // "Coefficient is null iff Missing is non-empty" invariant still holds
                    // instead of silently breaking.
                    missing.Add(new MissingIndexValue(Guid.Empty, mathResult.PendingReason ?? "Unresolved.", endPeriod));
                }
            }

            results.Add(new DueAdjustment(
                row.Id, dueDate, row.MonthlyRent, basePeriod, endPeriod, missing, coefficient, proposedCanon));
        }

        return results;
    }

    /// <summary>
    /// Resolves every index a clause references for both period ends. An index that fails to
    /// resolve (spec "Successor Resolution"), a splice across two different series (design
    /// Decision 7), or a period with no published level (spec "Missing Index Value Leaves the
    /// Adjustment Pending") all surface as a named <see cref="MissingIndexValue"/> rather than a
    /// fault — the adjustment stays pending, never guessed.
    /// </summary>
    private static (List<IndexVariationInput> Inputs, List<MissingIndexValue> Missing) ResolveIndices(
        AdjustmentClause clause,
        IReadOnlyDictionary<Guid, EconomicIndex> catalog,
        IReadOnlyDictionary<(Guid EconomicIndexId, IndexPeriod Period), decimal> levels,
        IndexPeriod basePeriod,
        IndexPeriod endPeriod)
    {
        var missing = new List<MissingIndexValue>();
        var inputs = new List<IndexVariationInput>(clause.Indices.Count);

        foreach (var reference in clause.Indices.OrderBy(i => i.Ordinal))
        {
            if (!catalog.TryGetValue(reference.EconomicIndexId, out var index))
            {
                missing.Add(new MissingIndexValue(reference.EconomicIndexId, "(unknown index)", endPeriod));
                continue;
            }

            if (IndexResolver.Resolve(index, basePeriod, catalog) is not IndexResolution.Resolved baseResolved)
            {
                missing.Add(new MissingIndexValue(index.Id, index.Name, basePeriod));
                continue;
            }

            if (IndexResolver.Resolve(index, endPeriod, catalog) is not IndexResolution.Resolved endResolved)
            {
                missing.Add(new MissingIndexValue(index.Id, index.Name, endPeriod));
                continue;
            }

            if (baseResolved.Index.Id != endResolved.Index.Id)
            {
                // The splice rule (design Decision 7): a rebase inside the window resolves the
                // base and end periods to two different series, and the ratio is refused. There
                // is no missing value in the usual sense, but the invariant still requires a
                // named entry, so this surfaces against the end period.
                missing.Add(new MissingIndexValue(index.Id, index.Name, endPeriod));
                continue;
            }

            var hasBaseLevel = levels.TryGetValue((baseResolved.Index.Id, basePeriod), out var baseLevel);
            var hasEndLevel = levels.TryGetValue((endResolved.Index.Id, endPeriod), out var endLevel);

            if (!hasBaseLevel)
            {
                missing.Add(new MissingIndexValue(index.Id, index.Name, basePeriod));
            }

            if (!hasEndLevel)
            {
                missing.Add(new MissingIndexValue(index.Id, index.Name, endPeriod));
            }

            if (!hasBaseLevel || !hasEndLevel)
            {
                continue;
            }

            inputs.Add(new IndexVariationInput(
                index.Id, index.Name,
                baseResolved.Index.Id, basePeriod, baseLevel,
                endResolved.Index.Id, endPeriod, endLevel));
        }

        return (inputs, missing);
    }
}
