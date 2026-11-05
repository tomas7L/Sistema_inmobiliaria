using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// <see cref="IContractRegistry"/> over <c>contracts</c> (gestion-de-alquileres spec Requirements 6 to 8, design §4). Creating is
/// one <c>SaveChanges</c> inside a transaction, with the units locked before their availability is read, so two operators
/// cannot both let the same unit. A cutover date is left to the database to refuse: the trigger decides who may set one, not
/// this class. Searching is done in memory by <see cref="AccountSearch"/>'s rule, as <see cref="AccountDirectory"/> does.
/// </summary>
public sealed class ContractRegistry : IContractRegistry
{
    private readonly InmobiliariaDbContext _context;
    private readonly IAgencyClock _clock;

    public ContractRegistry(InmobiliariaDbContext context, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        _context = context;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ContractListing>> ListAsync(string? text, ContractStatus? status, CancellationToken ct = default)
    {
        var query = status is { } wanted ? _context.Contracts.Where(c => c.Status == wanted) : _context.Contracts;
        var listings = await ListingsAsync(query, ct);

        return string.IsNullOrWhiteSpace(text)
            ? listings.OrderBy(l => AccountSearch.Fold(l.Tenants), StringComparer.Ordinal).ThenBy(l => l.Id).ToList()
            : AccountSearch.Select(listings, text, l => [l.Tenants, l.Lessors, l.Units], l => l.Tenants, l => l.Id.ToString("N"));
    }

    public async Task<ContractDetail?> DetailAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await _context.Contracts.AsNoTracking()
            .Include(c => c.Parties)
            .Include(c => c.Units)
            .Include(c => c.Adjustments)
            .Include(c => c.AdjustmentClause).ThenInclude(clause => clause!.Indices)
            .SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null)
        {
            return null;
        }

        var (parties, units) = await NamesAsync([contract], ct);

        return new ContractDetail(
            contract.Id,
            contract.Status,
            contract.StartDate,
            contract.NominalEndDate,
            IsPastEnd(contract),
            contract.NoticeGivenDate,
            contract.PlannedMoveOutDate,
            contract.ActualEndDate,
            contract.EndReason,
            contract.CutoverDate,
            contract.MonthlyRent,
            contract.DueDay,
            contract.HonorariosPercentage,
            contract.ContractualFeePlan,
            contract.Parties
                .Select(p => new ContractPartyLine(p.PartyId, CollectionDesk.DescribeParty(parties[p.PartyId]), p.Role))
                .OrderBy(p => p.Role)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList(),
            contract.Units
                .Select(u => new ContractUnitLine(u.UnitId, CollectionDesk.DescribeUnit(units[u.UnitId]), u.SharePercentage))
                .OrderBy(u => u.Address, StringComparer.Ordinal)
                .ToList(),
            await ClauseAsync(contract, ct),
            contract.Adjustments
                .OrderBy(a => a.EffectiveDate)
                .ThenBy(a => a.ConfirmedAt)
                .Select(a => new ContractAdjustmentLine(a.EffectiveDate, a.PreviousCanon, a.NewCanon, a.Coefficient, a.Kind))
                .ToList());
    }

    public async Task<ContractResult> CreateAsync(ContractDraft draft, bool acceptWarnings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var today = _clock.Today;
        if (ContractDraftRules.Check(draft, today) is { } refusal)
        {
            return ContractResult.Refused(refusal);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        var unitIds = draft.Shares.Select(s => s.UnitId).ToArray();
        await _context.Database.ExecuteSqlAsync($"SELECT 1 FROM units WHERE id = ANY({unitIds}) ORDER BY id FOR UPDATE", ct);

        var coverage = await new UnitRegistry(_context).ListAsync(ct);
        var named = coverage.Where(u => unitIds.Contains(u.Id)).ToList();
        if (named.Count != unitIds.Length)
        {
            return ContractResult.Refused(ContractRefusal.UnitNotFound);
        }

        if (named.FirstOrDefault(u => u.Covering?.Status == ContractStatus.Active) is { } rented)
        {
            return ContractResult.Refused(ContractRefusal.UnitUnderActiveContract, rented.Covering!.TenantName);
        }

        var lessorIds = draft.LessorIds.Distinct().ToList();
        var codebtorIds = draft.CodebtorIds.Distinct().ToList();
        var tenantId = draft.TenantId!.Value;
        var personIds = lessorIds.Concat(codebtorIds).Append(tenantId).Distinct().ToList();
        var persons = await _context.Parties.AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (persons.Count != personIds.Count)
        {
            return ContractResult.Refused(ContractRefusal.PartyNotFound);
        }

        if (draft.Clause is { } clauseDraft
            && await _context.EconomicIndices.CountAsync(i => clauseDraft.IndexIds.Contains(i.Id), ct) != clauseDraft.IndexIds.Count)
        {
            return ContractResult.Refused(ContractRefusal.IndexNotFound);
        }

        var warnings = ContractDraftRules.Warnings(draft, today)
            .Concat(named
                .Where(u => u.Covering?.Status == ContractStatus.PendingTermination)
                .Select(u => new ContractWarning(ContractWarningKind.UnitBeingVacated, u.Covering!.MoveOutDate, u.AddressText)))
            .ToList();
        if (warnings.Count > 0 && !acceptWarnings)
        {
            return ContractResult.NeedsAccepting(warnings);
        }

        var contract = Build(draft, persons, tenantId, lessorIds, codebtorIds);
        _context.Contracts.Add(contract);
        _context.ContractAccounts.Add(new ContractAccount(Guid.NewGuid(), contract.Id));

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (RefusalOf(ex) is { } databaseRefusal)
        {
            _context.ChangeTracker.Clear();
            return ContractResult.Refused(databaseRefusal);
        }

        // Read before committing, so failing here writes nothing rather than reporting a failure over a saved contract.
        var listing = (await ListingsAsync(_context.Contracts.Where(c => c.Id == contract.Id), ct)).Single();
        await transaction.CommitAsync(ct);

        return ContractResult.Saved(listing);
    }

    public Task<decimal?> SuggestRateAsync(Guid lessorId, CancellationToken ct = default) =>
        (from party in _context.ContractParties
         join contract in _context.Contracts on party.ContractId equals contract.Id
         where party.PartyId == lessorId && party.Role == PartyRole.Lessor && contract.HonorariosPercentage != null
         orderby contract.StartDate descending, contract.Id
         select contract.HonorariosPercentage)
        .FirstOrDefaultAsync(ct);

    private Contract Build(
        ContractDraft draft,
        Dictionary<Guid, Party> persons,
        Guid tenantId,
        IReadOnlyList<Guid> lessorIds,
        IReadOnlyList<Guid> codebtorIds)
    {
        var plan = draft is { FeePlanAmount: { } amount, FeePlanInstalments: { } instalments }
            ? new ContractualFeePlan(amount, instalments)
            : null;
        var contract = new Contract(
            Guid.NewGuid(),
            draft.StartDate!.Value,
            draft.NominalEndDate!.Value,
            draft.MonthlyRent!.Value,
            draft.Shares,
            draft.HonorariosPercentage,
            draft.DueDay,
            draft.CutoverDate,
            plan);

        contract.AssignParty(persons[tenantId], PartyRole.Tenant);
        foreach (var lessor in lessorIds)
        {
            contract.AssignParty(persons[lessor], PartyRole.Lessor);
        }

        foreach (var codebtor in codebtorIds)
        {
            contract.AssignParty(persons[codebtor], PartyRole.Codebtor);
        }

        if (draft.Clause is { } clauseDraft)
        {
            var clause = new AdjustmentClause(
                Guid.NewGuid(), contract.Id, clauseDraft.IndexIds, clauseDraft.IntervalMonths, RoundingRule.TruncateToWholePeso);
            _context.AdjustmentClauses.Add(clause);
            contract.AttachAdjustmentClause(clause);
        }

        return contract;
    }

    private async Task<List<ContractListing>> ListingsAsync(IQueryable<Contract> query, CancellationToken ct)
    {
        var contracts = await query.AsNoTracking().Include(c => c.Parties).Include(c => c.Units).ToListAsync(ct);
        var (parties, units) = await NamesAsync(contracts, ct);

        string Names(Contract contract, PartyRole role) => string.Join(
            ReciboRows.NameSeparator,
            contract.Parties
                .Where(p => p.Role == role)
                .Select(p => CollectionDesk.DescribeParty(parties[p.PartyId]))
                .Order(StringComparer.Ordinal));

        return contracts
            .Select(c => new ContractListing(
                c.Id,
                Names(c, PartyRole.Tenant),
                Names(c, PartyRole.Lessor),
                string.Join(
                    ReciboRows.NameSeparator,
                    c.Units.Select(u => CollectionDesk.DescribeUnit(units[u.UnitId])).Order(StringComparer.Ordinal)),
                c.Status,
                c.StartDate,
                c.NominalEndDate,
                IsPastEnd(c)))
            .ToList();
    }

    private async Task<(Dictionary<Guid, Party> Parties, Dictionary<Guid, Domain.Units.Unit> Units)> NamesAsync(
        IReadOnlyCollection<Contract> contracts, CancellationToken ct)
    {
        var partyIds = contracts.SelectMany(c => c.Parties).Select(p => p.PartyId).Distinct().ToList();
        var unitIds = contracts.SelectMany(c => c.Units).Select(u => u.UnitId).Distinct().ToList();

        return (
            await _context.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct),
            await _context.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct));
    }

    private async Task<ContractClauseLine?> ClauseAsync(Contract contract, CancellationToken ct)
    {
        if (contract.AdjustmentClause is not { } clause)
        {
            return null;
        }

        var indexIds = clause.Indices.Select(i => i.EconomicIndexId).ToList();
        var names = await _context.EconomicIndices.AsNoTracking()
            .Where(i => indexIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Name, ct);

        return new ContractClauseLine(
            clause.Indices.OrderBy(i => i.Ordinal).Select(i => names[i.EconomicIndexId]).ToList(),
            clause.IntervalMonths,
            clause.RoundingRule);
    }

    /// <summary>Flagged, never ended by this: a contract past its end stays as it is until someone ends it (spec Requirement 6).</summary>
    private bool IsPastEnd(Contract contract) => contract.Status != ContractStatus.Ended && _clock.Today > contract.NominalEndDate;

    private static ContractRefusal? RefusalOf(DbUpdateException ex) =>
        ex.InnerException is PostgresException database
            ? ContractRegistryErrorMap.For(database.SqlState, database.ConstraintName)
            : null;
}

/// <summary>
/// The database's refusals of a contract, keyed by SQLSTATE and constraint name and never by message text, like
/// <see cref="DeskErrorMap"/>. A pair not listed is null: it surfaces as an error instead of hiding a bug as an operator
/// message.
/// </summary>
public static class ContractRegistryErrorMap
{
    public static ContractRefusal? For(string? sqlState, string? constraintName) =>
        (sqlState, constraintName) switch
        {
            ("P0001", "contracts_cutover_insert_admin_only") => ContractRefusal.CutoverAdminOnly,
            _ => null,
        };
}
