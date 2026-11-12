using System.Net.Mail;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Notices;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="ITenantNoticeDesk"/> (notificaciones design §1, §3.3). Reading is the Mora list plus
/// the desk figure: one Mora read brings every account up to date and gives the days overdue (design D3), and each account's
/// total and recargo come from the cash desk's own <see cref="ContractAccount"/> methods (design D2), never a second formula.
/// Sending logs in once, before the database is touched, then gives each notice its own transaction: insert, send, and commit
/// only once the email was accepted (design D5, spec Decision 15).
/// </summary>
public sealed class TenantNoticeDesk : ITenantNoticeDesk
{
    private readonly InmobiliariaDbContext _context;
    private readonly IAgencyClock _clock;
    /// <summary>The unique index two PCs sending the same notice collide on (design D6).</summary>
    private const string UniqueNotice = "ux_tenant_notices_contract_kind_month";

    private readonly RecargoTerms _terms;
    private readonly ITenantNoticeSender _sender;

    public TenantNoticeDesk(InmobiliariaDbContext context, IAgencyClock clock, RecargoTerms terms, ITenantNoticeSender sender)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(sender);

        _context = context;
        _clock = clock;
        _terms = terms;
        _sender = sender;
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

    public async Task<NoticeSendReport> SendAsync(
        IReadOnlyList<TenantNotice> notices, MailboxCredentials credentials, string agencyName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(agencyName);

        if (notices.Count == 0)
        {
            return new NoticeSendReport([]);
        }

        // 1. Log in first. A refused login fails every notice with its reason, and the database is never touched (design D5).
        var opening = await _sender.OpenAsync(credentials, ct);
        if (opening.Session is not { } session)
        {
            var reason = opening.FailureReason ?? "No se pudo iniciar sesión en el correo.";
            return new NoticeSendReport(notices.Select(n => NoticeSendOutcome.Failed(n, reason)).ToList());
        }

        // 2. One notice at a time, each in its own transaction. A failure on one never stops the next (spec Requirement 5).
        var outcomes = new List<NoticeSendOutcome>(notices.Count);
        await using (session)
        {
            foreach (var notice in notices)
            {
                outcomes.Add(await SendOneAsync(session, notice, agencyName, credentials.AppPassword, ct));
            }
        }

        return new NoticeSendReport(outcomes);
    }

    private async Task<NoticeSendOutcome> SendOneAsync(
        IMailSession session, TenantNotice notice, string agencyName, string appPassword, CancellationToken ct)
    {
        _context.ChangeTracker.Clear();
        var accepted = false;

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            // The insert takes the unique index's lock: a second PC sending the same notice waits here until this
            // transaction ends, then fails as a duplicate or proceeds (design D6, spec Test 9).
            _context.TenantNotices.Add(new TenantNoticeEntry
            {
                Id = Guid.NewGuid(),
                ContractId = notice.ContractId,
                Kind = TenantNoticeEntry.AmountOwedKind,
                NoticeMonth = notice.NoticeMonth,
                Recipient = notice.Recipient,
                FiguresOn = notice.FiguresOn,
                Total = notice.Total,
                Recargo = notice.Recargo,
            });

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException exception) when (IsAlreadySent(exception))
            {
                // Rolled back as the transaction is disposed; no email goes out (spec Requirement 4).
                return NoticeSendOutcome.AlreadySent(notice);
            }

            var delivery = await session.SendAsync(NoticeComposer.Compose(notice, agencyName), ct);
            if (!delivery.Accepted)
            {
                // Rolled back as the transaction is disposed: a refused email leaves no row (spec Decision 15).
                return NoticeSendOutcome.Failed(notice, delivery.FailureReason ?? "El correo no fue aceptado.");
            }

            // Once Gmail accepted it, the email is out: record it whatever the caller's token says, or a cancelled press would
            // leave no row and the next press would send it again (Phase 2 verifier, advisory 1).
            accepted = true;
            await transaction.CommitAsync(CancellationToken.None);

            return NoticeSendOutcome.Sent(notice);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Nothing is reported as sent that was not; an email accepted but not recorded says so, so nobody resends it blind.
            var reason = SecretMask.Mask(exception.Message, appPassword);
            return NoticeSendOutcome.Failed(
                notice,
                accepted
                    ? $"El correo salió, pero no se pudo registrar el envío ({reason}). Revise antes de volver a enviarlo."
                    : $"No se pudo enviar: {reason}");
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    /// <summary>A unique violation on the sent record's index, read from the exception chain by SQLSTATE and name (design D6).</summary>
    private static bool IsAlreadySent(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueNotice })
            {
                return true;
            }
        }

        return false;
    }

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
