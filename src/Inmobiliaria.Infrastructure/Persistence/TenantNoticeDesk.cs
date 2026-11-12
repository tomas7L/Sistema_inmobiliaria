using System.Net.Mail;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Notices;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="ITenantNoticeDesk"/> (notificaciones design §1, §3.3). Reading is the Mora list plus
/// the desk figure: one Mora read brings every account up to date and gives the days overdue (design D3), and each account's
/// total and recargo come from the cash desk's own <see cref="ContractAccount"/> methods (design D2), never a second formula.
/// </summary>
public sealed class TenantNoticeDesk : ITenantNoticeDesk
{
    private readonly InmobiliariaDbContext _context;
    private readonly IAgencyClock _clock;
    private readonly RecargoTerms _terms;

    public TenantNoticeDesk(InmobiliariaDbContext context, IAgencyClock clock, RecargoTerms terms)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(terms);

        _context = context;
        _clock = clock;
        _terms = terms;
    }

    public async Task<NoticeBoard> ReadAsync(CancellationToken ct = default)
    {
        var today = _clock.Today;
        var month = TenantNotice.MonthOf(today);

        // The Mora read materialises every account first (design F3), so the figures below are the ones the desk would show.
        var daysOverdue = (await new MoraWorklistQuery(_context, new AccountMaterialiser(_context))
                .GetAccountsInMoraAsync(today, _terms, ct))
            .ToDictionary(row => row.AccountId, row => row.DaysOverdue);

        var accounts = await _context.ContractAccounts.AsNoTracking().Include(a => a.Movements).ToListAsync(ct);
        var listings = (await AccountListings.ReadAllAsync(_context, today, ct)).ToDictionary(e => e.Listing.AccountId);

        var sent = await _context.TenantNotices.AsNoTracking()
            .Where(n => n.Kind == TenantNoticeEntry.AmountOwedKind && n.NoticeMonth == month)
            .ToListAsync(ct);
        var sentContracts = sent.Select(n => n.ContractId).ToHashSet();

        var toSend = new List<TenantNotice>();
        var noEmail = new List<UnreachableTenant>();

        foreach (var account in accounts)
        {
            var entry = listings[account.Id];
            var listing = entry.Listing;

            // A loaded contract waits on carga-inicial (spec Decision 12); one cancelled as loaded by mistake never owed (13).
            if (listing.CutoverDate is not null || entry.LoadedByMistake || sentContracts.Contains(listing.ContractId))
            {
                continue;
            }

            var total = account.AmountOwedForPayment(today, _terms);
            if (total <= 0m)
            {
                continue;
            }

            if (UsableAddress(entry.TenantEmail) is not { } recipient)
            {
                noEmail.Add(new UnreachableTenant(listing.ContractShortId, listing.Tenants, listing.Units));
                continue;
            }

            toSend.Add(new TenantNotice(
                account.Id,
                listing.ContractId,
                listing.ContractShortId,
                listing.Tenants,
                listing.Units,
                recipient,
                today,
                total,
                total - account.LedgerForPayment(today),
                daysOverdue.GetValueOrDefault(account.Id),
                month));
        }

        var byContract = listings.Values.ToDictionary(e => e.Listing.ContractId, e => e.Listing);
        var alreadySent = sent
            .OrderBy(n => n.RecordedAt)
            .Select(n => new SentNotice(byContract[n.ContractId].ContractShortId, byContract[n.ContractId].Tenants, n.RecordedBy, n.RecordedAt))
            .ToList();

        // The Mora list's order: oldest debt first, then the tenant as the desk's search folds it.
        return new NoticeBoard(
            toSend
                .OrderByDescending(n => n.DaysOverdue)
                .ThenBy(n => AccountSearch.Fold(n.Tenant), StringComparer.Ordinal)
                .ThenBy(n => n.AccountId)
                .ToList(),
            noEmail.OrderBy(n => AccountSearch.Fold(n.Tenant), StringComparer.Ordinal).ToList(),
            alreadySent);
    }

    /// <summary>Phase 2 of notificaciones (tasks 2.3): reading is all this slice ships.</summary>
    public Task<NoticeSendReport> SendAsync(
        IReadOnlyList<TenantNotice> notices, MailboxCredentials credentials, CancellationToken ct = default) =>
        throw new NotSupportedException("Sending notices lands with notificaciones Phase 2 (tasks 2.3).");

    /// <summary>
    /// The address, when <paramref name="email"/> is present and shaped like one bare address (spec Decision 17); otherwise null.
    /// A display-name form such as "Ana &lt;ana@x.com&gt;" is not a bare address and is refused.
    /// </summary>
    private static string? UsableAddress(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();

        return MailAddress.TryCreate(trimmed, out var parsed) && parsed.Address == trimmed ? parsed.Address : null;
    }
}
