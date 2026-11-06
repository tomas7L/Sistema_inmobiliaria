using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// Changing a contract that exists (gestion-de-alquileres spec Requirements 9 and 10, design §2.3, D12 to D14). Every change
/// runs in one transaction under the account's lock, the one the desk and the lazy accrual take, so a correction cannot
/// overwrite the canon of a period being written: the trigger does not cover the canon, because a confirmed adjustment
/// legitimately changes it (D16).
/// </summary>
public sealed partial class ContractRegistry
{
    private sealed record Locked(Contract Contract, ContractAccount Account);

    public Task<ContractResult> CorrectAsync(Guid contractId, ContractCorrection correction, bool acceptWarnings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(correction);

        return ChangeAsync(contractId, locked => ApplyCorrectionAsync(locked, correction, acceptWarnings, ct), ct);
    }

    public Task<ContractResult> SetRateAsync(Guid contractId, decimal rate, CancellationToken ct = default)
    {
        if (!ContractDraftRules.IsValidRate(rate))
        {
            return Task.FromResult(ContractResult.Refused(ContractRefusal.HonorariosOutOfRange));
        }

        return ChangeAsync(
            contractId,
            locked =>
            {
                locked.Contract.SetHonorariosPercentage(rate);
                return Task.FromResult<ContractResult?>(null);
            },
            ct);
    }

    public Task<ContractResult> SetCodebtorsAsync(Guid contractId, IReadOnlyList<Guid> codebtorIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(codebtorIds);

        return ChangeAsync(contractId, locked => ApplyCodebtorsAsync(locked, codebtorIds.Distinct().ToList(), ct), ct);
    }

    public Task<ContractResult> GiveNoticeAsync(Guid contractId, DateOnly noticeDate, DateOnly moveOutDate, CancellationToken ct = default) =>
        ChangeAsync(
            contractId,
            locked =>
            {
                if (ContractLifecycleRules.CheckNotice(locked.Contract.Status, noticeDate, moveOutDate, _clock.Today) is { } refusal)
                {
                    return Task.FromResult<ContractResult?>(ContractResult.Refused(refusal));
                }

                locked.Contract.GiveNotice(noticeDate, moveOutDate);
                return Task.FromResult<ContractResult?>(null);
            },
            ct);

    public async Task<ContractEndSummary> SummarizeEndAsync(Guid contractId, EndReason? reason, DateOnly endDate, CancellationToken ct = default)
    {
        var contract = await _context.Contracts.AsNoTracking()
            .Include(c => c.Adjustments)
            .SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null)
        {
            return ContractEndSummary.Refused(ContractRefusal.ContractNotFound);
        }

        var today = _clock.Today;
        if (ContractLifecycleRules.CheckEnd(contract.Status, contract.StartDate, reason, endDate, today) is { } refusal)
        {
            return ContractEndSummary.Refused(refusal);
        }

        var account = await _context.ContractAccounts.AsNoTracking()
                .Include(a => a.Movements)
                .SingleOrDefaultAsync(a => a.ContractId == contractId, ct)
            ?? new ContractAccount(Guid.NewGuid(), contractId);

        var alreadyWritten = account.Movements
            .Where(m => m.Kind == MovementKind.RentAccrual && m.OccurredOn > endDate && m.Period is not null)
            .Select(m => m.Period!.Value)
            .Order()
            .ToList();

        // Nothing here is tracked or saved. The end is applied in memory so that the figure is what ending would leave. The figures
        // are the desk's, over every movement whatever its date: a voided early payment leaves rent dated ahead of today, and the
        // dated LedgerBalance and AmountOwed would call that nothing owed just before the desk asks for it.
        contract.End(reason, endDate);
        DueAccruals.AppendInMemory(account, contract, today);

        return new ContractEndSummary(
            null,
            account.Movements.Sum(m => m.Amount),
            _terms is { } terms ? account.AmountOwedForPayment(today, terms) : null,
            alreadyWritten);
    }

    public Task<ContractResult> EndAsync(Guid contractId, EndReason? reason, DateOnly endDate, CancellationToken ct = default) =>
        ChangeAsync(
            contractId,
            locked =>
            {
                if (ContractLifecycleRules.CheckEnd(locked.Contract.Status, locked.Contract.StartDate, reason, endDate, _clock.Today) is { } refusal)
                {
                    return Task.FromResult<ContractResult?>(ContractResult.Refused(refusal));
                }

                locked.Contract.End(reason, endDate);
                return Task.FromResult<ContractResult?>(null);
            },
            ct);

    /// <summary>One change in one transaction: <paramref name="change"/> answers with a result to stop on, which writes nothing, or null to save.</summary>
    private async Task<ContractResult> ChangeAsync(Guid contractId, Func<Locked, Task<ContractResult?>> change, CancellationToken ct)
    {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        if (await LockAsync(contractId, ct) is not { } locked)
        {
            return ContractResult.Refused(ContractRefusal.ContractNotFound);
        }

        try
        {
            if (await change(locked) is { } stopped)
            {
                return stopped;
            }

            await _context.SaveChangesAsync(ct);

            // Read before committing, so failing here writes nothing rather than reporting a failure over a saved change.
            var listing = (await ListingsAsync(_context.Contracts.Where(c => c.Id == contractId), ct)).Single();
            await transaction.CommitAsync(ct);

            return ContractResult.Saved(listing);
        }
        catch (Exception ex) when (DatabaseRefusalOf(ex) is { } refusal)
        {
            _context.ChangeTracker.Clear();
            return ContractResult.Refused(refusal);
        }
    }

    /// <summary>
    /// The account's lock first, in the desk's order, then the contract's row, for a contract that has no account to lock. Removed
    /// parties are loaded too, because assigning the same person again restores their row (<see cref="Contract.AssignParty"/>).
    /// </summary>
    private async Task<Locked?> LockAsync(Guid contractId, CancellationToken ct)
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

        var contract = await _context.Contracts
            .IgnoreQueryFilters()
            .Include(c => c.Parties)
            .Include(c => c.Units)
            .Include(c => c.Adjustments)
            .SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null)
        {
            return null;
        }

        var account = accountId is { } existing
            ? await _context.ContractAccounts.AsNoTracking().Include(a => a.Movements).SingleAsync(a => a.Id == existing, ct)
            : new ContractAccount(Guid.NewGuid(), contractId);

        return new Locked(contract, account);
    }

    private async Task<ContractResult?> ApplyCorrectionAsync(Locked locked, ContractCorrection correction, bool acceptWarnings, CancellationToken ct)
    {
        var (contract, account) = locked;
        var today = _clock.Today;

        if (WhyNotCorrectable(locked) is { } locks)
        {
            return ContractResult.Refused(locks);
        }

        // A correction is judged as a form is. The rate is its own operation, so it stays out of what is checked here.
        var draft = new ContractDraft
        {
            TenantId = correction.TenantId,
            LessorIds = correction.LessorIds,
            Shares = correction.Shares,
            StartDate = correction.StartDate,
            NominalEndDate = correction.NominalEndDate,
            MonthlyRent = correction.MonthlyRent,
            DueDay = correction.DueDay,
            CutoverDate = contract.CutoverDate,
            FeePlanAmount = correction.FeePlanAmount,
            FeePlanInstalments = correction.FeePlanInstalments,
        };
        if (ContractDraftRules.Check(draft, today, contract.StartDate) is { } refusal)
        {
            return ContractResult.Refused(refusal);
        }

        var currentUnits = contract.Units.Select(u => u.UnitId).ToList();
        if (correction.Shares.Count != currentUnits.Count)
        {
            return ContractResult.Refused(ContractRefusal.UnitCountChanged);
        }

        var newUnits = correction.Shares.Select(s => s.UnitId).Except(currentUnits).ToArray();
        await _context.Database.ExecuteSqlAsync($"SELECT 1 FROM units WHERE id = ANY({newUnits}) ORDER BY id FOR UPDATE", ct);
        var named = (await new UnitRegistry(_context).ListAsync(ct)).Where(u => newUnits.Contains(u.Id)).ToList();
        if (named.Count != newUnits.Length)
        {
            return ContractResult.Refused(ContractRefusal.UnitNotFound);
        }

        if (named.FirstOrDefault(u => u.Covering?.Status == ContractStatus.Active) is { } rented)
        {
            return ContractResult.Refused(ContractRefusal.UnitUnderActiveContract, rented.Covering!.TenantName);
        }

        var tenantId = correction.TenantId!.Value;
        var lessorIds = correction.LessorIds.Distinct().ToList();
        var personIds = lessorIds.Append(tenantId).Distinct().ToList();
        var persons = await _context.Parties.AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (persons.Count != personIds.Count)
        {
            return ContractResult.Refused(ContractRefusal.PartyNotFound);
        }

        // Only what this correction changes is news: a first due date already passed is not warned about again.
        var termsMoved = correction.StartDate != contract.StartDate || correction.DueDay != contract.DueDay;
        var warnings = ContractDraftRules.Warnings(draft, today)
            .Where(w => w.Kind == ContractWarningKind.FirstDueDatePassed && termsMoved)
            .Concat(named
                .Where(u => u.Covering?.Status == ContractStatus.PendingTermination)
                .Select(u => new ContractWarning(ContractWarningKind.UnitBeingVacated, u.Covering!.MoveOutDate, u.AddressText)))
            .ToList();
        if (warnings.Count > 0 && !acceptWarnings)
        {
            return ContractResult.NeedsAccepting(warnings);
        }

        var plan = draft is { FeePlanAmount: { } amount, FeePlanInstalments: { } instalments }
            ? new ContractualFeePlan(amount, instalments)
            : null;
        contract.Correct(correction.StartDate!.Value, correction.NominalEndDate!.Value, correction.MonthlyRent!.Value, correction.DueDay, plan, account);

        await ReplaceTenantAsync(locked, persons[tenantId], ct);
        ReplaceParties(locked, PartyRole.Lessor, lessorIds, persons);
        await _context.SaveChangesAsync(ct);
        await RewriteUnitsAsync(contract.Id, currentUnits, correction.Shares, ct);

        return null;
    }

    /// <summary>The reasons no correction can go ahead, whatever it says.</summary>
    private static ContractRefusal? WhyNotCorrectable(Locked locked)
    {
        if (locked.Contract.Status == ContractStatus.Ended)
        {
            return ContractRefusal.ContractEnded;
        }

        if (locked.Account.Movements.Count > 0)
        {
            return ContractRefusal.AccountHasMovements;
        }

        return locked.Contract.Adjustments.Count > 0 ? ContractRefusal.AdjustmentConfirmed : null;
    }

    /// <summary>
    /// Saves between taking one tenant off and putting another on: the tenant index is not deferrable, and EF orders two updates
    /// on one table by key, so bringing back a previous tenant could otherwise reach the database before the current one left.
    /// </summary>
    private async Task ReplaceTenantAsync(Locked locked, Party tenant, CancellationToken ct)
    {
        var current = locked.Contract.Parties.FirstOrDefault(p => p.Role == PartyRole.Tenant);
        if (current?.PartyId == tenant.Id)
        {
            return;
        }

        if (current is not null)
        {
            Remove(locked, current.PartyId, PartyRole.Tenant, replacementFollows: true);
            await _context.SaveChangesAsync(ct);
        }

        locked.Contract.AssignParty(tenant, PartyRole.Tenant);
    }

    /// <summary>Whoever joins goes on before whoever leaves comes off, so a role is never empty on the way.</summary>
    private void ReplaceParties(Locked locked, PartyRole role, IReadOnlyList<Guid> wanted, Dictionary<Guid, Party> persons)
    {
        var current = locked.Contract.Parties.Where(p => p.Role == role).Select(p => p.PartyId).ToList();

        foreach (var joining in wanted.Except(current))
        {
            locked.Contract.AssignParty(persons[joining], role);
        }

        foreach (var leaving in current.Except(wanted))
        {
            Remove(locked, leaving, role, replacementFollows: false);
        }
    }

    private void Remove(Locked locked, Guid partyId, PartyRole role, bool replacementFollows)
    {
        locked.Contract.RemoveParty(partyId, role, locked.Account, DateTimeOffset.UtcNow, replacementFollows);
        _context.ContractEvents.Add(new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid(),
            ["ContractId"] = locked.Contract.Id,
            ["Kind"] = nameof(ContractEventKind.PartyRemoved),
            ["Detail"] = $"{role} {partyId}",
        });
    }

    private async Task<ContractResult?> ApplyCodebtorsAsync(Locked locked, IReadOnlyList<Guid> wanted, CancellationToken ct)
    {
        var persons = await _context.Parties.AsNoTracking().Where(p => wanted.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (persons.Count != wanted.Count)
        {
            return ContractResult.Refused(ContractRefusal.PartyNotFound);
        }

        ReplaceParties(locked, PartyRole.Codebtor, wanted, persons);

        return null;
    }

    /// <summary>
    /// Raw <c>UPDATE</c>s, because <see cref="Contract.SetUnitShares"/> issues a DELETE neither role may run (design Finding F1, D14). A
    /// replaced unit keeps its row. The share-sum trigger is deferred, so it judges the result at commit and not each statement.
    /// </summary>
    private async Task RewriteUnitsAsync(Guid contractId, IReadOnlyList<Guid> currentUnits, IReadOnlyList<UnitShare> shares, CancellationToken ct)
    {
        var wanted = shares.Select(s => s.UnitId).ToList();
        var leaving = new Queue<Guid>(currentUnits.Except(wanted));

        foreach (var share in shares)
        {
            var row = currentUnits.Contains(share.UnitId) ? share.UnitId : leaving.Dequeue();
            await _context.Database.ExecuteSqlAsync(
                $"UPDATE contract_units SET unit_id = {share.UnitId}, share_percentage = {share.Percentage} WHERE contract_id = {contractId} AND unit_id = {row}",
                ct);
        }
    }

    private static ContractRefusal? DatabaseRefusalOf(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException) is { } database
            ? ContractRegistryErrorMap.For(database.SqlState, database.ConstraintName)
            : null;
}
