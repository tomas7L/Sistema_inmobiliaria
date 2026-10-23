using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.GoLive;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="IGoLiveSetup"/> (ui-de-cobranza design section 5.3, D25). The
/// cutover date goes through <see cref="Contract.CheckCutoverDate"/> and <see cref="Contract.SetCutoverDate"/>
/// and the starting number through the existing <see cref="ReciboNumbering"/>, so neither rule is
/// written twice. A refusal from the domain or a named one from the database comes back as a value; any
/// other failure surfaces as an error, because turning an unknown failure into a refusal would hide a bug.
/// </summary>
public sealed class GoLiveSetup : IGoLiveSetup
{
    private readonly InmobiliariaDbContext _context;
    private readonly IAgencyClock _clock;

    public GoLiveSetup(InmobiliariaDbContext context, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        _context = context;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CutoverListing>> ListContractsAsync(CancellationToken ct = default)
    {
        var contracts = await _context.Contracts.AsNoTracking()
            .Include(c => c.Parties)
            .Include(c => c.Units)
            .ToListAsync(ct);

        var partyIds = contracts.SelectMany(c => c.Parties).Select(p => p.PartyId).Distinct().ToList();
        var unitIds = contracts.SelectMany(c => c.Units).Select(u => u.UnitId).Distinct().ToList();
        var parties = await _context.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var units = await _context.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        // Locked from the first movement of any kind: the rule of Contract.CheckCutoverDate and the trigger.
        var accountHasMovement = await _context.ContractAccounts.AsNoTracking()
            .Select(a => new { a.ContractId, HasMovement = a.Movements.Any() })
            .ToDictionaryAsync(a => a.ContractId, a => a.HasMovement, ct);

        return contracts
            .Select(c => new CutoverListing(
                c.Id,
                string.Join(CollectionDesk.NameSeparator, c.Parties
                    .Where(p => p.Role == PartyRole.Tenant)
                    .Select(p => CollectionDesk.DescribeParty(parties[p.PartyId]))
                    .Order(StringComparer.Ordinal)),
                string.Join(CollectionDesk.NameSeparator, c.Units
                    .Select(u => CollectionDesk.DescribeUnit(units[u.UnitId]))
                    .Order(StringComparer.Ordinal)),
                c.StartDate,
                c.CutoverDate,
                accountHasMovement.TryGetValue(c.Id, out var hasMovement)
                    ? hasMovement ? CutoverState.Locked : CutoverState.Changeable
                    : CutoverState.NoAccount))
            .OrderBy(l => l.Tenants, StringComparer.CurrentCulture)
            .ThenBy(l => l.ContractId)
            .ToList();
    }

    public async Task<SetupResult> SetCutoverDateAsync(Guid contractId, DateOnly cutoverDate, CancellationToken ct = default)
    {
        var contract = await _context.Contracts.SingleOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new InvalidOperationException($"Contract {contractId} does not exist.");

        var account = await _context.ContractAccounts
            .Include(a => a.Movements)
            .SingleOrDefaultAsync(a => a.ContractId == contractId, ct);

        if (account is null)
        {
            return SetupResult.Refused(SetupRefusal.NoAccount);
        }

        var today = _clock.Today;
        if (contract.CheckCutoverDate(cutoverDate, account, today) is { } refusal)
        {
            return SetupResult.Refused(refusal switch
            {
                CutoverRefusal.AfterToday => SetupRefusal.CutoverAfterToday,
                CutoverRefusal.BeforeContractStart => SetupRefusal.CutoverBeforeContractStart,
                CutoverRefusal.AccountHasMovement => SetupRefusal.CutoverLocked,
                _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "An unmapped cutover refusal."),
            });
        }

        contract.SetCutoverDate(cutoverDate, account, today);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (RefusalOf(exception, SetupErrorMap.ForCutover) is { } refused)
        {
            // A first movement that arrived between the check and the save, or a session outside
            // inmobiliaria_admin (spec Requirement 18, last scenario; Requirement 22).
            return SetupResult.Refused(refused);
        }

        return SetupResult.Success;
    }

    public async Task<StartingNumberState> GetStartingNumberStateAsync(CancellationToken ct = default)
    {
        var startingNumber = await _context.Database
            .SqlQuery<int?>($"""SELECT starting_number AS "Value" FROM recibo_numbering WHERE id = 1""")
            .SingleAsync(ct);
        var anyRecibo = await _context.Database
            .SqlQuery<bool>($"""SELECT EXISTS (SELECT 1 FROM recibos) AS "Value" """)
            .SingleAsync(ct);

        return new StartingNumberState(startingNumber, anyRecibo);
    }

    public async Task<SetupResult> SetStartingNumberAsync(int startingNumber, CancellationToken ct = default)
    {
        // ReciboNumbering refuses this with an exception; the screen should hear it as a refusal.
        if (startingNumber < 1)
        {
            return SetupResult.Refused(SetupRefusal.StartingNumberOutOfRange);
        }

        try
        {
            await new ReciboNumbering(_context).SetStartingNumberAsync(startingNumber, ct);
        }
        catch (Exception exception) when (RefusalOf(exception, SetupErrorMap.ForStartingNumber) is { } refused)
        {
            return SetupResult.Refused(refused);
        }

        return SetupResult.Success;
    }

    private static SetupRefusal? RefusalOf(Exception exception, Func<string?, string?, SetupRefusal?> map)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg)
            {
                return map(pg.SqlState, pg.ConstraintName);
            }
        }

        return null;
    }
}

/// <summary>
/// The database's refusals of the two go-live statements, keyed by SQLSTATE and constraint name and never
/// by message text, like <see cref="DeskErrorMap"/>. One table per statement, so a <c>42501</c> means "not
/// Admin" only where the column grant raises it. Strings in, so it is tested without a database.
/// </summary>
public static class SetupErrorMap
{
    public static SetupRefusal? ForCutover(string? sqlState, string? constraintName) =>
        (sqlState, constraintName) switch
        {
            ("P0001", "contracts_cutover_locked") => SetupRefusal.CutoverLocked,
            ("P0001", "contracts_cutover_admin_only") => SetupRefusal.CutoverAdminOnly,
            _ => null,
        };

    public static SetupRefusal? ForStartingNumber(string? sqlState, string? constraintName) =>
        (sqlState, constraintName) switch
        {
            ("P0001", "recibo_numbering_cutover_locked") => SetupRefusal.StartingNumberLocked,
            ("23514", "ck_recibo_numbering_starting_positive") => SetupRefusal.StartingNumberOutOfRange,
            ("42501", _) => SetupRefusal.StartingNumberAdminOnly,
            _ => null,
        };
}
