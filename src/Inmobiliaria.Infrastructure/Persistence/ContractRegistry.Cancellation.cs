using System.Globalization;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// Cancelling a contract loaded by mistake and undoing a mistaken notice or end (gestion-de-alquileres spec Requirements 23 and 24, design
/// §2.4). Each runs through <see cref="ChangeAsync"/>, under the account's lock. A cancellation plans again there and compares with the
/// summary the operator confirmed, as a voiding does, so what is written is what was read.
/// </summary>
public sealed partial class ContractRegistry
{
    public async Task<ContractCancellationSummary> SummarizeCancelAsync(Guid contractId, string? reason, CancellationToken ct = default)
    {
        var contract = await _context.Contracts.AsNoTracking()
            .Include(c => c.Adjustments)
            .SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null)
        {
            return ContractCancellationSummary.Refused(ContractRefusal.ContractNotFound);
        }

        var account = await _context.ContractAccounts.AsNoTracking()
                .Include(a => a.Movements)
                .SingleOrDefaultAsync(a => a.ContractId == contractId, ct)
            ?? new ContractAccount(Guid.NewGuid(), contractId);

        return await SummaryOfAsync(contract, account, reason, ct);
    }

    public Task<ContractResult> CancelAsync(Guid contractId, string? reason, ContractCancellationSummary shown, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(shown);

        return ChangeAsync(
            contractId,
            async locked =>
            {
                var fresh = await SummaryOfAsync(locked.Contract, locked.Account, reason, ct);
                if (fresh.Refusal is { } refusal)
                {
                    return ContractResult.Refused(refusal) with { RefusalText = fresh.RefusalText };
                }

                if (shown.Refusal is not null || fresh.Fingerprint != shown.Fingerprint || fresh.AmountStoppedBeingOwed != shown.AmountStoppedBeingOwed)
                {
                    return ContractResult.Refused(ContractRefusal.CancellationStale);
                }

                await WriteCancellationAsync(locked, reason!, fresh, ct);

                return null;
            },
            ct);
    }

    public Task<ContractResult> UndoNoticeAsync(Guid contractId, string? reason, CancellationToken ct = default) =>
        ChangeAsync(
            contractId,
            locked =>
            {
                var contract = locked.Contract;
                if (ContractLifecycleRules.CheckUndoNotice(contract.Status, reason) is { } refusal)
                {
                    return Task.FromResult<ContractResult?>(ContractResult.Refused(refusal));
                }

                RecordEvent(contract.Id, ContractEventKind.NoticeUndone, reason, $"{Day(contract.NoticeGivenDate)} {Day(contract.PlannedMoveOutDate)}");
                contract.UndoNotice();

                return Task.FromResult<ContractResult?>(null);
            },
            ct);

    public async Task<ContractUndoEndSummary> SummarizeUndoEndAsync(Guid contractId, string? reason, CancellationToken ct = default)
    {
        var contract = await _context.Contracts.AsNoTracking()
            .Include(c => c.Units)
            .Include(c => c.Adjustments)
            .SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null)
        {
            return new ContractUndoEndSummary(ContractRefusal.ContractNotFound, null, []);
        }

        if (ContractLifecycleRules.CheckUndoEnd(contract.Status, contract.EndReason, reason) is { } refusal)
        {
            return new ContractUndoEndSummary(refusal, null, []);
        }

        if (await TenantCoveringAsync(contract, ct) is { } tenant)
        {
            return new ContractUndoEndSummary(ContractRefusal.UnitCoveredByAnotherContract, tenant, []);
        }

        var account = await _context.ContractAccounts.AsNoTracking()
                .Include(a => a.Movements)
                .SingleOrDefaultAsync(a => a.ContractId == contractId, ct)
            ?? new ContractAccount(Guid.NewGuid(), contractId);

        // Nothing here is tracked or saved. The end is undone in memory so that the schedule says what would come due.
        contract.UndoEnd();

        return new ContractUndoEndSummary(
            null,
            null,
            AccrualSchedule.PeriodsDue(contract, _clock.Today)
                .Where(p => !account.IsChargedFor(p.Period))
                .Select(p => p.Period)
                .ToList());
    }

    public Task<ContractResult> UndoEndAsync(Guid contractId, string? reason, CancellationToken ct = default) =>
        ChangeAsync(
            contractId,
            async locked =>
            {
                var contract = locked.Contract;
                if (ContractLifecycleRules.CheckUndoEnd(contract.Status, contract.EndReason, reason) is { } refusal)
                {
                    return ContractResult.Refused(refusal);
                }

                // Locked before they are read, as on creating, or a contract being created now could let one of them too.
                var unitIds = contract.Units.Select(u => u.UnitId).ToArray();
                await _context.Database.ExecuteSqlAsync($"SELECT 1 FROM units WHERE id = ANY({unitIds}) ORDER BY id FOR UPDATE", ct);
                if (await TenantCoveringAsync(contract, ct) is { } tenant)
                {
                    return ContractResult.Refused(ContractRefusal.UnitCoveredByAnotherContract, tenant);
                }

                RecordEvent(contract.Id, ContractEventKind.EndUndone, reason, $"{contract.EndReason} {Day(contract.ActualEndDate)}");
                contract.UndoEnd();

                return null;
            },
            ct);

    /// <summary>What cancelling means now. Appends what is due to <paramref name="account"/> in memory, so that account is never saved.</summary>
    private async Task<ContractCancellationSummary> SummaryOfAsync(Contract contract, ContractAccount account, string? reason, CancellationToken ct)
    {
        var today = _clock.Today;
        var plan = ContractCancellation.Plan(account, contract, await CollectionsOfAsync(account.Id, ct), reason, today);
        if (plan.Refusal is { } refusal)
        {
            return new ContractCancellationSummary(refusal, string.Empty, string.Empty, 0m, plan.RecibosToVoid, plan.OnlyAnAdminCanVoid, 0m, [], string.Empty);
        }

        // What stops being owed is what the desk would ask for, periods never written included: they stop being owed too. Only the
        // plan's corrections reach the ledger, since those periods were never there.
        DueAccruals.AppendInMemory(account, contract, today);
        var owed = _terms is { } terms ? account.AmountOwedForPayment(today, terms) : account.Movements.Sum(m => m.Amount);
        var listing = (await ListingsAsync(_context.Contracts.Where(c => c.Id == contract.Id), ct)).Single();

        return new ContractCancellationSummary(
            null,
            listing.Tenants,
            listing.Units,
            owed,
            [],
            false,
            plan.RetainedAmount,
            plan.RetainedRecibos,
            FingerprintOf(plan));
    }

    private static string FingerprintOf(ContractCancellationPlan plan) => string.Join(
        ";",
        plan.Corrections
            .Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.CorrectsMovementId}:{c.Amount}:{c.OccurredOn:yyyy-MM-dd}:{c.Period:yyyy-MM-dd}"))
            .Order(StringComparer.Ordinal));

    private async Task WriteCancellationAsync(Locked locked, string reason, ContractCancellationSummary summary, CancellationToken ct)
    {
        var contract = locked.Contract;
        var today = _clock.Today;

        // Planned over the tracked account, so the corrections it appends are the ones saved. The plan is pure, and the fingerprint
        // has just said it comes out the same.
        var account = await _context.ContractAccounts.Include(a => a.Movements).SingleOrDefaultAsync(a => a.ContractId == contract.Id, ct);
        var reversed = 0m;
        if (account is not null)
        {
            var plan = ContractCancellation.Plan(account, contract, await CollectionsOfAsync(account.Id, ct), reason, today);
            reversed = -plan.Corrections.Sum(c => c.Amount);
            foreach (var correction in plan.Corrections)
            {
                account.Append(correction);
            }
        }

        // The end reason it replaces is about to be gone from the contract's row, so this is the only place left that holds it.
        var replaced = contract.EndReason is { } earlier ? $"; Replaced {earlier}" : string.Empty;
        RecordEvent(
            contract.Id,
            ContractEventKind.Cancelled,
            reason,
            string.Create(CultureInfo.InvariantCulture, $"Reversed {reversed:0.##}; Stopped being owed {summary.AmountStoppedBeingOwed}{replaced}"));
        contract.Cancel(today);
    }

    /// <summary>Every payment on the account with what its voiding window needs. Whether it was voided is read from its recibos' annulments.</summary>
    private async Task<IReadOnlyList<CancellationCollection>> CollectionsOfAsync(Guid accountId, CancellationToken ct)
    {
        var collections = await _context.Collections.AsNoTracking()
            .Where(c => EF.Property<Guid>(c, "AccountId") == accountId)
            .ToListAsync(ct);
        var ids = collections.Select(c => ReciboRows.Read<Guid>(c, "Id")).ToList();
        var recibos = await _context.Recibos.AsNoTracking()
            .Where(r => ids.Contains(EF.Property<Guid>(r, "CollectionId")))
            .ToListAsync(ct);
        var reciboIds = recibos.Select(r => ReciboRows.Read<Guid>(r, "Id")).ToList();
        var annulled = (await _context.ReciboAnnulments.AsNoTracking()
                .Where(a => reciboIds.Contains(EF.Property<Guid>(a, "ReciboId")))
                .ToListAsync(ct))
            .Select(a => ReciboRows.Read<Guid>(a, "ReciboId"))
            .ToHashSet();

        return collections
            .Select(collection =>
            {
                var own = recibos.Where(r => ReciboRows.Read<Guid>(r, "CollectionId") == ReciboRows.Read<Guid>(collection, "Id")).ToList();
                return new CancellationCollection(
                    ReciboRows.Read<Guid>(collection, "PaymentMovementId"),
                    ReciboRows.AgencyDateOf(ReciboRows.Read<DateTimeOffset>(collection, "RecordedAt")),
                    own.Select(r => ReciboRows.Read<int>(r, "Number")).Order().ToList(),
                    own.Any(r => annulled.Contains(ReciboRows.Read<Guid>(r, "Id"))));
            })
            .ToList();
    }

    /// <summary>Who rents one of this contract's units under another live contract, or null. This one is ended, so it never covers its own.</summary>
    private async Task<string?> TenantCoveringAsync(Contract contract, CancellationToken ct)
    {
        var unitIds = contract.Units.Select(u => u.UnitId).ToHashSet();
        var covered = (await new UnitRegistry(_context).ListAsync(ct)).FirstOrDefault(u => unitIds.Contains(u.Id) && u.Covering is not null);

        return covered?.Covering!.TenantName;
    }

    private void RecordEvent(Guid contractId, ContractEventKind kind, string? reason, string? detail)
    {
        var row = new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid(),
            ["ContractId"] = contractId,
            ["Kind"] = kind.ToString(),
        };
        if (reason is not null)
        {
            row["Reason"] = reason;
        }

        if (detail is not null)
        {
            row["Detail"] = detail;
        }

        _context.ContractEvents.Add(row);
    }

    private static string Day(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
}
