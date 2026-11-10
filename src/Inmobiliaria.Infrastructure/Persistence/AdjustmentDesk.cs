using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// <see cref="IAdjustmentDesk"/> over one context (gestion-de-alquileres design §3.4, D8). The figures come from
/// <see cref="DueAdjustmentQuery"/>, so the worklist, the summary and what is written cannot disagree; confirming rebuilds
/// them under the account's lock and then the contract's row, the order every other writer takes, and refuses unless they
/// match what the operator saw. The partial unique indexes of M6 back the one check two sessions could both pass.
/// </summary>
public sealed class AdjustmentDesk : IAdjustmentDesk
{
    private const string OneRegularPerDueDate = "ux_rent_adjustments_one_regular_per_due_date";

    private readonly InmobiliariaDbContext _context;
    private readonly IAgencyClock _clock;
    private readonly Guid _confirmedBy;

    /// <param name="context">The session's context.</param>
    /// <param name="clock">The agency's day, from which the applied-from period is derived.</param>
    /// <param name="confirmedBy">The signed-in user, recorded on every adjustment this desk writes.</param>
    public AdjustmentDesk(InmobiliariaDbContext context, IAgencyClock clock, Guid confirmedBy)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        _context = context;
        _clock = clock;
        _confirmedBy = confirmedBy;
    }

    public async Task<AdjustmentPreviewResult> PreviewAsync(Guid contractId, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();

        var contract = await _context.Contracts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null)
        {
            return AdjustmentPreviewResult.Refused(AdjustmentRefusal.ContractNotFound);
        }

        var account = await AccountOfAsync(contractId, ct);
        return (await BuildAsync(contract, account, ct)).Result;
    }

    public async Task<AdjustmentConfirmResult> ConfirmAsync(AdjustmentPreview preview, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            var accountId = await _context.ContractAccounts.AsNoTracking()
                .Where(a => a.ContractId == preview.ContractId)
                .Select(a => (Guid?)a.Id)
                .SingleOrDefaultAsync(ct);
            if (accountId is { } id)
            {
                await AccountRowLock.TakeAsync(_context, id, ct);
            }

            await _context.Database.ExecuteSqlAsync($"SELECT 1 FROM contracts WHERE id = {preview.ContractId} FOR UPDATE", ct);

            // Everything below is read after both locks, the clause included, so a clause correction or a payment that
            // committed while this waited is seen, not priced over (tasks.md 8.9, for S9).
            var contract = await _context.Contracts
                .Include(c => c.Adjustments)
                .SingleOrDefaultAsync(c => c.Id == preview.ContractId, ct);
            if (contract is null)
            {
                return AdjustmentConfirmResult.Refused(AdjustmentRefusal.ContractNotFound);
            }

            if (contract.Adjustments.Any(a => a.Kind == AdjustmentKind.Regular && a.EffectiveDate == preview.DueDate))
            {
                return AdjustmentConfirmResult.Refused(AdjustmentRefusal.AlreadyConfirmed);
            }

            var account = await AccountOfAsync(preview.ContractId, ct);
            var (rebuilt, proposal) = await BuildAsync(contract, account, ct);
            if (rebuilt.Preview is not { } current || proposal is null)
            {
                return AdjustmentConfirmResult.Refused(rebuilt.Refusal ?? AdjustmentRefusal.NotDue);
            }

            if (!current.SameFiguresAs(preview))
            {
                return AdjustmentConfirmResult.Refused(AdjustmentRefusal.PreviewStale);
            }

            var adjustment = contract.ConfirmAdjustment(
                Guid.NewGuid(), proposal, DateTimeOffset.UtcNow, _confirmedBy, appliesFrom: current.AppliesFrom);
            await _context.SaveChangesAsync(ct);

            // Read before committing, so failing here writes nothing rather than reporting a failure over a saved change.
            var written = (await HistoryOfAsync(contract.Id, ct)).Single(h => h.Id == adjustment.Id);
            await transaction.CommitAsync(ct);

            return AdjustmentConfirmResult.Done(written);
        }
        catch (Exception ex) when (IsDoubleConfirmation(ex))
        {
            _context.ChangeTracker.Clear();
            return AdjustmentConfirmResult.Refused(AdjustmentRefusal.AlreadyConfirmed);
        }
    }

    public Task<IReadOnlyList<AdjustmentHistoryEntry>> HistoryAsync(Guid contractId, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();
        return HistoryOfAsync(contractId, ct);
    }

    private async Task<(AdjustmentPreviewResult Result, AdjustmentProposal? Proposal)> BuildAsync(
        Contract contract, ContractAccount account, CancellationToken ct)
    {
        var today = _clock.Today;
        var due = (await new DueAdjustmentQuery(_context).GetDueAsync(today, contract.Id, ct)).SingleOrDefault();
        if (due is null)
        {
            return (AdjustmentPreviewResult.Refused(AdjustmentRefusal.NotDue), null);
        }

        if (due.Missing.Count > 0 || due.Coefficient is not { } coefficient)
        {
            return (AdjustmentPreviewResult.Refused(AdjustmentRefusal.IndexValuesMissing, due.Missing), null);
        }

        var proposal = new AdjustmentProposal(due.CurrentCanon, due.IndexValues, due.Combination, coefficient, due.DueDate);
        var names = await NamesAsync(due.IndexValues.Select(v => v.ResolvedIndexId), ct);

        var preview = new AdjustmentPreview(
            contract.Id,
            await TenantNameAsync(contract.Id, ct),
            due.DueDate,
            due.CurrentCanon,
            Lines(due.IndexValues, names),
            due.Combination,
            coefficient,
            proposal.UntruncatedNewCanon,
            proposal.TruncatedNewCanon,
            AdjustmentTiming.AppliesFrom(contract, account, today));

        return (AdjustmentPreviewResult.Ready(preview), proposal);
    }

    private async Task<IReadOnlyList<AdjustmentHistoryEntry>> HistoryOfAsync(Guid contractId, CancellationToken ct)
    {
        var rows = await _context.RentAdjustments.AsNoTracking()
            .Include(a => a.IndexValues)
            .Where(a => a.ContractId == contractId)
            .ToListAsync(ct);

        var confirmerIds = rows.Where(a => a.ConfirmedBy is not null).Select(a => a.ConfirmedBy!.Value).Distinct().ToList();
        var confirmers = await _context.AppUsers.AsNoTracking()
            .Where(u => confirmerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var names = await NamesAsync(rows.SelectMany(a => a.IndexValues).Select(v => v.ResolvedIndexId), ct);

        return rows
            .OrderByDescending(a => a.ConfirmedAt)
            .ThenByDescending(a => a.EffectiveDate)
            .Select(a => new AdjustmentHistoryEntry(
                a.Id,
                a.EffectiveDate,
                a.AppliesFrom,
                a.PreviousCanon,
                a.NewCanon,
                a.Coefficient,
                a.Kind,
                a.ConfirmedAt,
                a.ConfirmedBy is { } by ? confirmers.GetValueOrDefault(by) : null,
                Lines(a.IndexValues, names)))
            .ToList();
    }

    private async Task<ContractAccount> AccountOfAsync(Guid contractId, CancellationToken ct) =>
        await _context.ContractAccounts.AsNoTracking().Include(a => a.Movements).SingleOrDefaultAsync(a => a.ContractId == contractId, ct)
        ?? new ContractAccount(Guid.NewGuid(), contractId);

    private async Task<string?> TenantNameAsync(Guid contractId, CancellationToken ct) =>
        await _context.ContractParties.AsNoTracking()
            .Where(cp => cp.ContractId == contractId && cp.Role == PartyRole.Tenant)
            .Join(_context.Parties.AsNoTracking(), cp => cp.PartyId, p => p.Id, (cp, p) => p.FirstName + " " + p.LastName)
            .FirstOrDefaultAsync(ct);

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> indexIds, CancellationToken ct)
    {
        var ids = indexIds.Distinct().ToList();
        return await _context.EconomicIndices.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
    }

    private static List<AdjustmentIndexLine> Lines(IEnumerable<RentAdjustmentIndexValue> values, IReadOnlyDictionary<Guid, string> names) =>
        values
            .Select(v => new AdjustmentIndexLine(
                names.GetValueOrDefault(v.ResolvedIndexId, "(índice desconocido)"), v.BasePeriod, v.BaseLevel, v.EndPeriod, v.EndLevel, v.Variation))
            .ToList();

    private static bool IsDoubleConfirmation(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException) is
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OneRegularPerDueDate };
}
