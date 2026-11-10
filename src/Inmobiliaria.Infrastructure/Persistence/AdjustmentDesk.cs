using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Indices;
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
    private const string OneCorrectionPerAdjustment = "ux_rent_adjustments_one_correction_per_adjustment";

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

    public async Task<AdjustmentConfirmResult> ConfirmAsync(AdjustmentPreview preview, bool chargeDifference = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            await LockAsync(preview.ContractId, ct);

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

            // Tracked, so a difference charged is saved with the adjustment, in this one transaction (spec Decision 19).
            var account = await _context.ContractAccounts.Include(a => a.Movements).SingleOrDefaultAsync(a => a.ContractId == preview.ContractId, ct)
                ?? new ContractAccount(Guid.NewGuid(), preview.ContractId);
            var (rebuilt, proposal) = await BuildAsync(contract, account, ct);
            if (rebuilt.Preview is not { } current || proposal is null)
            {
                return AdjustmentConfirmResult.Refused(rebuilt.Refusal ?? AdjustmentRefusal.NotDue);
            }

            if (!current.SameFiguresAs(preview))
            {
                return AdjustmentConfirmResult.Refused(AdjustmentRefusal.PreviewStale);
            }

            if (chargeDifference && current.Difference is null)
            {
                return AdjustmentConfirmResult.Refused(AdjustmentRefusal.NoDifferenceToCharge);
            }

            Guid? chargeId = null;
            if (chargeDifference && current.Difference is { } offer)
            {
                var charge = AccountMovement.AdjustmentDifference(
                    Guid.NewGuid(), offer.Amount, current.AppliesFrom, AccrualSchedule.DueDateFor(current.AppliesFrom, contract.DueDay));
                if (_context.Entry(account).State == EntityState.Detached)
                {
                    _context.ContractAccounts.Add(account);
                }

                account.Append(charge);
                chargeId = charge.Id;
            }

            var adjustment = contract.ConfirmAdjustment(
                Guid.NewGuid(), proposal, DateTimeOffset.UtcNow, _confirmedBy, appliesFrom: current.AppliesFrom,
                differenceAmount: current.Difference?.Amount, differenceMovementId: chargeId);
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

    public async Task<AdjustmentPreviewResult> PreviewCorrectionAsync(Guid contractId, Guid adjustmentId, CancellationToken ct = default)
    {
        _context.ChangeTracker.Clear();

        var contract = await _context.Contracts.AsNoTracking()
            .Include(c => c.Adjustments).ThenInclude(a => a.IndexValues)
            .SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null)
        {
            return AdjustmentPreviewResult.Refused(AdjustmentRefusal.ContractNotFound);
        }

        return (await BuildCorrectionAsync(contract, await AccountOfAsync(contractId, ct), adjustmentId, ct)).Result;
    }

    public async Task<AdjustmentConfirmResult> ConfirmCorrectionAsync(AdjustmentPreview preview, bool chargeDifference = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.CorrectsAdjustmentId is not { } corrected)
        {
            return AdjustmentConfirmResult.Refused(AdjustmentRefusal.AdjustmentNotFound);
        }

        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            await LockAsync(preview.ContractId, ct);

            var contract = await _context.Contracts
                .Include(c => c.Adjustments).ThenInclude(a => a.IndexValues)
                .SingleOrDefaultAsync(c => c.Id == preview.ContractId, ct);
            if (contract is null)
            {
                return AdjustmentConfirmResult.Refused(AdjustmentRefusal.ContractNotFound);
            }

            if (contract.Adjustments.Any(a => a.CorrectsAdjustmentId == corrected))
            {
                return AdjustmentConfirmResult.Refused(AdjustmentRefusal.AlreadyConfirmed);
            }

            var account = await _context.ContractAccounts.Include(a => a.Movements).SingleOrDefaultAsync(a => a.ContractId == preview.ContractId, ct)
                ?? new ContractAccount(Guid.NewGuid(), preview.ContractId);
            var built = await BuildCorrectionAsync(contract, account, corrected, ct);
            if (built.Result.Preview is not { } current || built.Proposal is null)
            {
                return AdjustmentConfirmResult.Refused(built.Result.Refusal ?? AdjustmentRefusal.NotDue);
            }

            if (!current.SameFiguresAs(preview))
            {
                return AdjustmentConfirmResult.Refused(AdjustmentRefusal.PreviewStale);
            }

            if (chargeDifference && current.Difference is null)
            {
                return AdjustmentConfirmResult.Refused(AdjustmentRefusal.NoDifferenceToCharge);
            }

            if (_context.Entry(account).State == EntityState.Detached && (built.Reversed is not null || chargeDifference))
            {
                _context.ContractAccounts.Add(account);
            }

            // An unpaid difference the corrected adjustment charged is taken back in this same transaction, dated no earlier than
            // the charge itself, so no day shows a credit from it (spec Requirement 19, Test 92).
            if (built.Reversed is { } reversed)
            {
                account.Append(AccountMovement.Correction(
                    Guid.NewGuid(), -built.ReversedAmount, LedgerDating.CorrectionDate(_clock.Today, reversed.OccurredOn), reversed.Id, reversed.Period));
            }

            Guid? chargeId = null;
            if (chargeDifference && current.Difference is { } offer)
            {
                var charge = AccountMovement.AdjustmentDifference(
                    Guid.NewGuid(), offer.Amount, current.AppliesFrom, AccrualSchedule.DueDateFor(current.AppliesFrom, contract.DueDay));
                account.Append(charge);
                chargeId = charge.Id;
            }

            var adjustment = contract.ConfirmAdjustment(
                Guid.NewGuid(), built.Proposal, DateTimeOffset.UtcNow, _confirmedBy, AdjustmentKind.Correction, corrected,
                current.AppliesFrom, current.Difference?.Amount, chargeId);
            await _context.SaveChangesAsync(ct);

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

    private sealed record CorrectionBuild(
        AdjustmentPreviewResult Result, AdjustmentProposal? Proposal = null, AccountMovement? Reversed = null, decimal ReversedAmount = 0m);

    /// <summary>
    /// The correction of <paramref name="adjustmentId"/> as it stands: the latest adjustment only (spec Decision 26), its own window
    /// and previous canon priced from today's levels, applying from today's period. A live difference the chain charged is either
    /// reversed, when its period is unpaid, with the choice offered again on the corrected figures, or left, when paid, with no new
    /// charge offered (spec Decision 27).
    /// </summary>
    private async Task<CorrectionBuild> BuildCorrectionAsync(Contract contract, ContractAccount account, Guid adjustmentId, CancellationToken ct)
    {
        if (contract.Status == ContractStatus.Ended)
        {
            return new(AdjustmentPreviewResult.Refused(AdjustmentRefusal.ContractEnded));
        }

        if (contract.Adjustments.SingleOrDefault(a => a.Id == adjustmentId) is not { } target)
        {
            return new(AdjustmentPreviewResult.Refused(AdjustmentRefusal.AdjustmentNotFound));
        }

        var latest = contract.Adjustments.OrderBy(a => a.ConfirmedAt).Last();
        if (latest.Id != target.Id)
        {
            return new(AdjustmentPreviewResult.Refused(AdjustmentRefusal.NotLatest));
        }

        var chain = new List<RentAdjustment>();
        for (RentAdjustment? row = target; row is not null; row = row.CorrectsAdjustmentId is { } earlier ? contract.Adjustments.SingleOrDefault(a => a.Id == earlier) : null)
        {
            chain.Add(row);
        }

        // The same months and the same series as the copy the corrected adjustment kept; only the levels are read again.
        var indexIds = target.IndexValues.Select(v => v.ResolvedIndexId).Distinct().ToList();
        var levels = await _context.IndexValues.AsNoTracking()
            .Where(v => indexIds.Contains(v.EconomicIndexId))
            .ToDictionaryAsync(v => (v.EconomicIndexId, v.Period), v => v.Level, ct);
        var names = await NamesAsync(indexIds, ct);

        var missing = new List<MissingIndexValue>();
        var inputs = new List<IndexVariationInput>();
        foreach (var used in target.IndexValues)
        {
            var name = names.GetValueOrDefault(used.ResolvedIndexId, "(índice desconocido)");
            var hasBase = levels.TryGetValue((used.ResolvedIndexId, used.BasePeriod), out var baseLevel);
            var hasEnd = levels.TryGetValue((used.ResolvedIndexId, used.EndPeriod), out var endLevel);
            if (!hasBase)
            {
                missing.Add(new MissingIndexValue(used.ResolvedIndexId, name, used.BasePeriod));
            }

            if (!hasEnd)
            {
                missing.Add(new MissingIndexValue(used.ResolvedIndexId, name, used.EndPeriod));
            }

            if (hasBase && hasEnd)
            {
                inputs.Add(new IndexVariationInput(
                    used.ReferencedIndexId, name, used.ResolvedIndexId, used.BasePeriod, baseLevel, used.ResolvedIndexId, used.EndPeriod, endLevel));
            }
        }

        // The same rule the clause has, which is defined by its number of indices (AdjustmentClause.Combination): one alone, or
        // their average. The copy keeps one value per index, so its count is the clause's.
        var combination = target.IndexValues.Count == 1 ? CombinationRule.Single : CombinationRule.Average;
        if (missing.Count > 0 || AdjustmentMath.Compute(combination, inputs) is not { Coefficient: { } coefficient } math)
        {
            return new(AdjustmentPreviewResult.Refused(AdjustmentRefusal.IndexValuesMissing, missing));
        }

        var proposal = new AdjustmentProposal(target.PreviousCanon, math.IndexValues, combination, coefficient, target.EffectiveDate);
        var appliesFrom = AdjustmentTiming.AppliesFrom(contract, account, _clock.Today);

        // The difference the chain charged and that is still live: the charge net of any correction naming it.
        var charged = chain
            .Where(a => a.DifferenceMovementId is not null)
            .Select(a => account.Movements.FirstOrDefault(m => m.Id == a.DifferenceMovementId))
            .Where(m => m is not null)
            .Select(m => (Charge: m!, Live: account.Movements.Where(x => x.Id == m!.Id || x.CorrectsMovementId == m!.Id).Sum(x => x.Amount)))
            .FirstOrDefault(c => c.Live > 0m);
        var paid = charged.Charge is { Period: { } period } && account.HasLivePaymentFor(period);

        // A difference offered and declined is final for that adjustment (spec Requirement 16; decided by Tomás on 2026-11-10): the
        // latest choice in the chain is what stands. Charged and still unpaid, it is reversed and offered again (Requirement 18).
        var declined = chain.FirstOrDefault(a => a.DifferenceAmount is not null) is { DifferenceMovementId: null };

        var preview = new AdjustmentPreview(
            contract.Id,
            await TenantNameAsync(contract.Id, ct),
            target.EffectiveDate,
            target.PreviousCanon,
            Lines(math.IndexValues, names),
            combination,
            coefficient,
            proposal.UntruncatedNewCanon,
            proposal.TruncatedNewCanon,
            appliesFrom,
            paid || declined ? null : AdjustmentDifference.ForCorrection(contract, account, chain, appliesFrom, proposal.TruncatedNewCanon))
        {
            CorrectsAdjustmentId = target.Id,
            ReversedDifference = charged.Charge is not null && !paid ? charged.Live : null,
            PaidDifferenceStays = paid ? charged.Live : null,
        };

        return new(AdjustmentPreviewResult.Ready(preview), proposal, paid ? null : charged.Charge, paid ? 0m : charged.Live);
    }

    /// <summary>The account's lock first, then the contract's row: the order every other writer takes.</summary>
    private async Task LockAsync(Guid contractId, CancellationToken ct)
    {
        var accountId = await _context.ContractAccounts.AsNoTracking()
            .Where(a => a.ContractId == contractId)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(ct);
        if (accountId is { } id)
        {
            await AccountRowLock.TakeAsync(_context, id, ct);
        }

        await _context.Database.ExecuteSqlAsync($"SELECT 1 FROM contracts WHERE id = {contractId} FOR UPDATE", ct);
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
        var appliesFrom = AdjustmentTiming.AppliesFrom(contract, account, today);

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
            appliesFrom,
            AdjustmentDifference.Regular(contract, account, due.DueDate, appliesFrom, due.CurrentCanon, proposal.TruncatedNewCanon));

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
                Lines(a.IndexValues, names))
            {
                DifferenceAmount = a.DifferenceAmount,
                DifferenceCharged = a.DifferenceMovementId is not null,
            })
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
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OneRegularPerDueDate or OneCorrectionPerAdjustment };
}
