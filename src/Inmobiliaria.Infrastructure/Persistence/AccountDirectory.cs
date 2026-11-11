using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="IAccountDirectory"/> (ui-de-cobranza design section 5.1). It reads
/// every account through <see cref="AccountListings"/>, the same text the mora worklist shows, and never
/// materialises accruals (unlike <see cref="MoraWorklistQuery"/>): a search writes nothing. The text match is
/// <see cref="AccountSearch"/>, in memory.
/// </summary>
public sealed class AccountDirectory : IAccountDirectory
{
    private readonly InmobiliariaDbContext _context;
    private readonly IAgencyClock _clock;

    public AccountDirectory(InmobiliariaDbContext context, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        _context = context;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AccountListing>> SearchAsync(string text, CancellationToken ct = default)
    {
        if (!AccountSearch.IsSearchable(text))
        {
            return [];
        }

        // A contract loaded by mistake is not offered for collection; one that ended for any other reason stays, since it can owe.
        var listings = (await AccountListings.ReadAllAsync(_context, _clock.Today, ct))
            .Where(e => !e.LoadedByMistake)
            .Select(e => e.Listing);

        return AccountSearch.Select(
            listings,
            text,
            l => [l.Tenants, l.Lessors, l.Units, l.ContractShortId],
            l => l.Tenants,
            l => l.AccountId.ToString("N"));
    }

    public async Task<AccountListing?> FindAsync(Guid accountId, CancellationToken ct = default) =>
        (await AccountListings.ReadAllAsync(_context, _clock.Today, ct))
            .Where(e => !e.LoadedByMistake && e.Listing.AccountId == accountId)
            .Select(e => e.Listing)
            .SingleOrDefault();
}
