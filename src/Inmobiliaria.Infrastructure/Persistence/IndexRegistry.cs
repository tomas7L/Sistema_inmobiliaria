using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// <see cref="IIndexRegistry"/> over <c>economic_indices</c> and <c>index_values</c> (gestion-de-alquileres spec Requirements 11
/// and 12, design §4). A correction overwrites the level and the database records what it overwrote, so nothing here writes
/// that record. A period is compared in memory, since <see cref="IndexPeriod"/>'s ordering has no SQL translation.
/// </summary>
public sealed class IndexRegistry : IIndexRegistry
{
    private const string OneValuePerMonthIndex = "ix_index_values_economic_index_id_period";

    private readonly InmobiliariaDbContext _context;
    private readonly IAgencyClock _clock;

    public IndexRegistry(InmobiliariaDbContext context, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        _context = context;
        _clock = clock;
    }

    public async Task<IReadOnlyList<IndexListing>> ListAsync(CancellationToken ct = default)
    {
        var indices = await _context.EconomicIndices.AsNoTracking().ToListAsync(ct);
        var values = await _context.IndexValues.AsNoTracking().ToListAsync(ct);

        return indices
            .OrderBy(index => index.Name, StringComparer.Ordinal)
            .ThenBy(index => index.Id)
            .Select(index => new IndexListing(
                index.Id,
                index.Name,
                index.DiscontinuedFrom,
                values
                    .Where(value => value.EconomicIndexId == index.Id)
                    .OrderByDescending(value => value.Period)
                    .Select(Listing)
                    .ToList()))
            .ToList();
    }

    public async Task<IndexEntryResult> AddAsync(Guid indexId, IndexPeriod period, decimal level, bool acceptFarLevel, CancellationToken ct = default)
    {
        if (IndexEntryRules.CheckPeriod(period, _clock.Today) is { } periodRefusal)
        {
            return IndexEntryResult.Refused(periodRefusal);
        }

        if (IndexEntryRules.CheckLevel(level) is { } levelRefusal)
        {
            return IndexEntryResult.Refused(levelRefusal);
        }

        if (!await _context.EconomicIndices.AnyAsync(index => index.Id == indexId, ct))
        {
            return IndexEntryResult.Refused(IndexRefusal.IndexNotFound);
        }

        var values = await ValuesOfAsync(indexId, ct);
        if (values.FirstOrDefault(value => value.Period == period) is { } existing)
        {
            return IndexEntryResult.Refused(IndexRefusal.DuplicatePeriod, existing);
        }

        var earlier = IndexEntryRules.NearestEarlier(period, values);
        if (!acceptFarLevel && IndexEntryRules.IsFar(level, period, earlier))
        {
            return IndexEntryResult.Refused(IndexRefusal.FarLevelNotAccepted, earlier);
        }

        var value = new IndexValue(Guid.NewGuid(), indexId, period, level);
        _context.IndexValues.Add(value);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OneValuePerMonthIndex })
        {
            // Someone else typed this month between the read above and this write.
            _context.ChangeTracker.Clear();
            var typed = (await ValuesOfAsync(indexId, ct)).FirstOrDefault(other => other.Period == period);

            return IndexEntryResult.Refused(IndexRefusal.DuplicatePeriod, typed);
        }

        return IndexEntryResult.Saved(Listing(value));
    }

    public async Task<IndexEntryResult> CorrectAsync(Guid valueId, decimal newLevel, bool acceptFarLevel, CancellationToken ct = default)
    {
        if (IndexEntryRules.CheckLevel(newLevel) is { } refusal)
        {
            return IndexEntryResult.Refused(refusal);
        }

        var value = await _context.IndexValues.SingleOrDefaultAsync(v => v.Id == valueId, ct);
        if (value is null)
        {
            return IndexEntryResult.Refused(IndexRefusal.ValueNotFound);
        }

        var previous = value.Level;
        if (newLevel == previous)
        {
            return IndexEntryResult.Saved(Listing(value), previous);
        }

        var earlier = IndexEntryRules.NearestEarlier(value.Period, await ValuesOfAsync(value.EconomicIndexId, ct));
        if (!acceptFarLevel && IndexEntryRules.IsFar(newLevel, value.Period, earlier))
        {
            return IndexEntryResult.Refused(IndexRefusal.FarLevelNotAccepted, earlier);
        }

        value.Correct(newLevel);
        await _context.SaveChangesAsync(ct);

        return IndexEntryResult.Saved(Listing(value), previous);
    }

    public async Task<IReadOnlyList<AffectedAdjustment>> ListAffectedAdjustmentsAsync(Guid valueId, decimal newLevel, CancellationToken ct = default)
    {
        var value = await _context.IndexValues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == valueId, ct);
        if (value is null)
        {
            return [];
        }

        var adjustments = await _context.RentAdjustments
            .AsNoTracking()
            .Include(adjustment => adjustment.IndexValues)
            .Where(adjustment => adjustment.IndexValues.Any(copy =>
                copy.ResolvedIndexId == value.EconomicIndexId
                && (copy.BasePeriod == value.Period || copy.EndPeriod == value.Period)))
            .ToListAsync(ct);

        var affected = adjustments
            .SelectMany(adjustment => adjustment.IndexValues
                .Where(copy => copy.ResolvedIndexId == value.EconomicIndexId)
                .SelectMany(copy => UsedLevels(copy, value.Period))
                .Where(used => used != newLevel)
                .Distinct()
                .Select(used => (Adjustment: adjustment, Used: used)))
            .ToList();

        var contractIds = affected.Select(row => row.Adjustment.ContractId).Distinct().ToList();
        var tenants = await (
                from contractParty in _context.ContractParties
                join party in _context.Parties on contractParty.PartyId equals party.Id
                where contractIds.Contains(contractParty.ContractId) && contractParty.Role == PartyRole.Tenant
                select new { contractParty.ContractId, party.FirstName, party.LastName })
            .AsNoTracking()
            .ToListAsync(ct);

        return affected
            .OrderByDescending(row => row.Adjustment.EffectiveDate)
            .ThenBy(row => row.Adjustment.Id)
            .Select(row => new AffectedAdjustment(
                row.Adjustment.Id,
                row.Adjustment.ContractId,
                tenants.Where(t => t.ContractId == row.Adjustment.ContractId).Select(t => $"{t.FirstName} {t.LastName}").FirstOrDefault(),
                row.Adjustment.EffectiveDate,
                row.Used))
            .ToList();
    }

    public async Task<IReadOnlyList<IndexCorrectionRecord>> ListCorrectionsAsync(Guid valueId, CancellationToken ct = default)
    {
        var rows = await _context.IndexValueCorrections
            .AsNoTracking()
            .Where(row => EF.Property<Guid>(row, "IndexValueId") == valueId)
            .ToListAsync(ct);

        return rows
            .Select(row => new IndexCorrectionRecord(
                ReciboRows.Read<decimal>(row, "PreviousLevel"),
                ReciboRows.Read<decimal>(row, "NewLevel"),
                ReciboRows.Read<string>(row, "RecordedBy"),
                ReciboRows.InAgencyZone(ReciboRows.Read<DateTimeOffset>(row, "RecordedAt"))))
            .OrderByDescending(record => record.RecordedAt)
            .ToList();
    }

    private async Task<List<IndexValueListing>> ValuesOfAsync(Guid indexId, CancellationToken ct)
    {
        var values = await _context.IndexValues.AsNoTracking().Where(value => value.EconomicIndexId == indexId).ToListAsync(ct);

        return values.Select(Listing).ToList();
    }

    private static IndexValueListing Listing(IndexValue value) => new(value.Id, value.Period, value.Level);

    /// <summary>The level a copy holds for <paramref name="period"/>, as its base, its end, or both.</summary>
    private static IEnumerable<decimal> UsedLevels(RentAdjustmentIndexValue copy, IndexPeriod period)
    {
        if (copy.BasePeriod == period)
        {
            yield return copy.BaseLevel;
        }

        if (copy.EndPeriod == period)
        {
            yield return copy.EndLevel;
        }
    }
}
