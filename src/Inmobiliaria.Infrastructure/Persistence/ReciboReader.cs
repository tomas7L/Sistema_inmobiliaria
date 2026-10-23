using Inmobiliaria.Domain.Collection;
using Microsoft.EntityFrameworkCore;
using static Inmobiliaria.Infrastructure.Persistence.ReciboRows;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="IReciboReader"/> (ui-de-cobranza design section 5.2). Recibos are
/// rebuilt from their rows with <see cref="ReciboRows"/>, the mapper the desk issues them through, so what is
/// displayed is what was issued: header snapshot, lines and total, never recomputed from the contract. Times
/// are converted through the agency's zone here; Presentation never knows a zone. Reads only, no tracking.
/// </summary>
public sealed class ReciboReader : IReciboReader
{
    private readonly InmobiliariaDbContext _context;

    public ReciboReader(InmobiliariaDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<IssuedPayment?> FindByNumberAsync(long reciboNumber, CancellationToken ct = default)
    {
        // The column is integer: a number it cannot hold is not looked for (design section 5.2).
        if (reciboNumber < 1 || reciboNumber > int.MaxValue)
        {
            return null;
        }

        var number = (int)reciboNumber;
        var collectionIds = await _context.Recibos.AsNoTracking()
            .Where(r => EF.Property<int>(r, "Number") == number)
            .Select(r => EF.Property<Guid>(r, "CollectionId"))
            .ToListAsync(ct);

        return collectionIds.Count == 0 ? null : await GetByCollectionAsync(collectionIds.Single(), ct);
    }

    public async Task<IssuedPayment?> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default)
    {
        var collection = await _context.Collections.AsNoTracking()
            .SingleOrDefaultAsync(c => EF.Property<Guid>(c, "Id") == collectionId, ct);
        if (collection is null)
        {
            return null;
        }

        var rows = await _context.Recibos.AsNoTracking()
            .Where(r => EF.Property<Guid>(r, "CollectionId") == collectionId)
            .ToListAsync(ct);
        var ids = rows.Select(r => Read<Guid>(r, "Id")).ToList();
        var lines = await _context.ReciboLines.AsNoTracking()
            .Where(l => ids.Contains(EF.Property<Guid>(l, "ReciboId")))
            .ToListAsync(ct);

        IssuedRecibo Copy(ReciboCopy copy)
        {
            var row = rows.Single(r => Read<string>(r, "Copy") == copy.ToString());
            var id = Read<Guid>(row, "Id");
            return new IssuedRecibo(id, ReciboFrom(row, lines.Where(l => Read<Guid>(l, "ReciboId") == id)));
        }

        var recordedAt = InAgencyZone(Read<DateTimeOffset>(collection, "RecordedAt"));

        return new IssuedPayment(
            collectionId,
            AgencyDateOf(recordedAt),
            recordedAt,
            Copy(ReciboCopy.Locatario),
            Copy(ReciboCopy.Propietario),
            await AnnulmentOfAsync(ids, ct));
    }

    public async Task<IReadOnlyList<RecentCollection>> RecentAsync(int limit, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var collections = await _context.Collections.AsNoTracking()
            .OrderByDescending(c => EF.Property<DateTimeOffset>(c, "RecordedAt"))
            .ThenByDescending(c => EF.Property<Guid>(c, "Id"))
            .Take(limit)
            .ToListAsync(ct);
        var collectionIds = collections.Select(c => Read<Guid>(c, "Id")).ToList();

        var recibos = await _context.Recibos.AsNoTracking()
            .Where(r => collectionIds.Contains(EF.Property<Guid>(r, "CollectionId")))
            .ToListAsync(ct);
        var reciboIds = recibos.Select(r => Read<Guid>(r, "Id")).ToList();
        var annulled = (await _context.ReciboAnnulments.AsNoTracking()
                .Where(a => reciboIds.Contains(EF.Property<Guid>(a, "ReciboId")))
                .Select(a => EF.Property<Guid>(a, "ReciboId"))
                .ToListAsync(ct))
            .ToHashSet();

        return collections.Select(c =>
        {
            var id = Read<Guid>(c, "Id");
            var pair = recibos.Where(r => Read<Guid>(r, "CollectionId") == id).ToList();
            var locatario = pair.Single(r => Read<string>(r, "Copy") == nameof(ReciboCopy.Locatario));
            var propietario = pair.Single(r => Read<string>(r, "Copy") == nameof(ReciboCopy.Propietario));

            return new RecentCollection(
                id,
                Read<int>(locatario, "Number"),
                Read<int>(propietario, "Number"),
                Read<string>(locatario, "TenantName"),
                Read<DateOnly>(c, "Period"),
                Read<DateOnly>(c, "ReceivedOn"),
                InAgencyZone(Read<DateTimeOffset>(c, "RecordedAt")),
                pair.Any(r => annulled.Contains(Read<Guid>(r, "Id"))));
        }).ToList();
    }

    /// <summary>
    /// The pair's annulment, if any: both recibos are annulled together with one reason, so the first by time
    /// speaks for the pair. <c>annulled_by</c> is a role name; it is shown as that user's display name, or as
    /// the role name when no application user has it (design D26).
    /// </summary>
    private async Task<AnnulmentRecord?> AnnulmentOfAsync(IReadOnlyList<Guid> reciboIds, CancellationToken ct)
    {
        var annulment = await _context.ReciboAnnulments.AsNoTracking()
            .Where(a => reciboIds.Contains(EF.Property<Guid>(a, "ReciboId")))
            .OrderBy(a => EF.Property<DateTimeOffset>(a, "AnnulledAt"))
            .FirstOrDefaultAsync(ct);
        if (annulment is null)
        {
            return null;
        }

        var role = Read<string>(annulment, "AnnulledBy");
        var displayName = await _context.AppUsers.AsNoTracking()
            .Where(u => u.Username == role)
            .Select(u => u.DisplayName)
            .SingleOrDefaultAsync(ct);

        return new AnnulmentRecord(
            Read<string>(annulment, "Reason"),
            displayName ?? role,
            InAgencyZone(Read<DateTimeOffset>(annulment, "AnnulledAt")));
    }
}
