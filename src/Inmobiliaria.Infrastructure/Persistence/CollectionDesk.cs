using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="ICollectionDesk"/>: the transaction of design section 2 and the
/// voiding of 7.5. The planners decide what to write; this reads the account, writes it atomically and
/// turns the database's refusals back into the planners' own.
///
/// One commit is one READ COMMITTED transaction: reload, rebuild the preview and compare it, then the
/// movements, the collection, the counter and the recibos, in that order. The counter comes last so that
/// every issuance queues on its one row for as short a time as possible, and so that a payment refused for
/// any other reason never touches it; being an UPDATE in the same transaction, a rollback gives the number
/// back (PV5, proved by Tests 15 and 16).
///
/// The five recibo-side tables are property-bag entities (tasks.md "Issues found" 9): the bags are touched
/// only in this file, through the small private helpers at the end, which build the domain types from
/// them and them from the domain types. Nothing outside this file knows what a bag is. Every
/// <c>PostgresException</c> is read here too, by SQLSTATE and constraint name; no Npgsql type leaves it.
/// </summary>
public sealed class CollectionDesk : ICollectionDesk
{
    /// <summary>
    /// Several tenants, lessors or units on one recibo are one text column each. A name containing this
    /// separator would be split in two when a voiding rebuilds the header, which only matters to that
    /// in-memory header, never to the document already issued.
    /// </summary>
    private const string NameSeparator = "; ";

    private static readonly TimeZoneInfo AgencyZone = AgencyClock.ResolveZone();

    private readonly InmobiliariaDbContext _context;
    private readonly IAgencyClock _clock;
    private readonly RecargoTerms _terms;

    public CollectionDesk(InmobiliariaDbContext context, IAgencyClock clock, RecargoTerms terms)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(terms);

        _context = context;
        _clock = clock;
        _terms = terms;
    }

    public async Task<CollectionPreview> PreviewAsync(CollectionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Nothing is tracked and nothing is saved: the periods already due are materialised in memory
        // only (design D3), so a preview that is never confirmed leaves no accrual behind.
        var loaded = await LoadAsync(request.AccountId, track: false, ct);

        return BuildPreview(loaded, request, _clock.Today);
    }

    public async Task<CollectionResult> CommitAsync(
        CollectionPreview preview, CollectionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(request);

        // A refused preview can never be committed; the refusal it carries is the answer.
        if (preview.Refusal is { } shownRefusal)
        {
            return CollectionResult.Refused(shownRefusal);
        }

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            _context.ChangeTracker.Clear();

            var loaded = await LoadAsync(request.AccountId, track: true, ct);
            var fresh = BuildPreview(loaded, request, _clock.Today);

            if (CollectionPlanner.Confirm(preview, fresh) is { } refusal)
            {
                return CollectionResult.Refused(refusal);
            }

            var period = fresh.Period!.Value;
            var account = loaded.Account;

            // 1. The movements, in period order: the accruals the account was missing (already appended by
            //    BuildPreview), then what the payment itself writes.
            var written = CollectionPlanner.MovementsToWrite(account, loaded.Contract, fresh);
            foreach (var movement in written)
            {
                account.Append(movement);
            }

            await _context.SaveChangesAsync(ct);

            // 2. The collection. Its attempt is the previous one plus one; the unique index is what two
            //    operators racing for the same attempt collide on.
            var payment = written.Single(m => m.Kind == MovementKind.Payment);
            var recargo = written.SingleOrDefault(m => m.Kind == MovementKind.RecargoCharged);
            var attempts = await _context.Collections.AsNoTracking()
                .CountAsync(c => EF.Property<Guid>(c, "AccountId") == account.Id && EF.Property<DateOnly>(c, "Period") == period, ct);
            var collectionId = Guid.NewGuid();

            _context.Collections.Add(CollectionBag(collectionId, account.Id, period, attempts + 1, fresh.ReceivedOn, payment.Id, recargo?.Id));
            await _context.SaveChangesAsync(ct);

            // 3. The counter, last, as the same transaction's raw UPDATE ... RETURNING. No row back means
            //    the starting number was never set: nothing can be issued, and the rollback undoes the rest.
            if (await TakeNextPairAsync(ct) is not { } firstNumber)
            {
                return CollectionResult.Refused(CollectionRefusal.CutoverNotSet);
            }

            // 4. The recibos, one save each, locatario then propietario.
            var pair = ReciboPair.Issue(
                firstNumber, period, fresh.ReceivedOn, loaded.Header, new ReciboDraftPair(fresh.Locatario!, fresh.Propietario!));

            foreach (var recibo in pair.Recibos)
            {
                AddRecibo(recibo, collectionId, loaded.Contract.Id);
                await _context.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);

            return CollectionResult.Committed(collectionId, firstNumber);
        }
        catch (Exception ex) when (CommitRefusalOf(ex) is { } refusal)
        {
            return CollectionResult.Refused(refusal);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    public async Task<VoidingPreview> PreviewVoidAsync(
        Guid reciboId, string reason, UserRole actingRole, CancellationToken ct = default)
    {
        var named = await _context.Recibos.AsNoTracking()
            .SingleOrDefaultAsync(r => EF.Property<Guid>(r, "Id") == reciboId, ct)
            ?? throw new InvalidOperationException($"No recibo exists with id '{reciboId}'.");

        var collectionId = Read<Guid>(named, "CollectionId");
        var collection = await _context.Collections.AsNoTracking()
            .SingleAsync(c => EF.Property<Guid>(c, "Id") == collectionId, ct);

        // Both copies, whichever was named (spec Decision 19), each with its lines.
        var rows = await _context.Recibos.AsNoTracking()
            .Where(r => EF.Property<Guid>(r, "CollectionId") == collectionId)
            .ToListAsync(ct);
        var ids = rows.Select(r => Read<Guid>(r, "Id")).ToList();
        var lines = await _context.ReciboLines.AsNoTracking()
            .Where(l => ids.Contains(EF.Property<Guid>(l, "ReciboId")))
            .ToListAsync(ct);
        var isAnnulled = await _context.ReciboAnnulments.AsNoTracking()
            .AnyAsync(a => ids.Contains(EF.Property<Guid>(a, "ReciboId")), ct);

        var pair = rows
            .Select(row => ReciboFrom(row, lines.Where(l => Read<Guid>(l, "ReciboId") == Read<Guid>(row, "Id"))))
            .ToList();
        var record = CollectionFrom(collection, isAnnulled);

        var account = (await LoadAccountAsync(record.AccountId, track: false, ct));

        // When the payment was recorded is the database's answer, converted through the agency's zone;
        // it is never something the operator supplies (design 7.5).
        var recordedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Read<DateTimeOffset>(collection, "RecordedAt"), AgencyZone).DateTime);

        return VoidingPlanner.Plan(account, record, pair, reason, _clock.Today, _terms, actingRole, recordedOn);
    }

    public async Task<VoidingResult> VoidAsync(VoidingPreview preview, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (preview.Refusal is { } shownRefusal)
        {
            return VoidingResult.Refused(shownRefusal);
        }

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            _context.ChangeTracker.Clear();

            var numbers = preview.Annulments.Select(a => checked((int)a.ReciboNumber)).ToList();
            var rows = await _context.Recibos.AsNoTracking()
                .Where(r => numbers.Contains(EF.Property<int>(r, "Number")))
                .ToListAsync(ct);

            if (rows.Count != numbers.Count)
            {
                throw new InvalidOperationException("The voiding names a recibo that does not exist.");
            }

            var collectionId = Read<Guid>(rows[0], "CollectionId");
            var collection = await _context.Collections.AsNoTracking()
                .SingleAsync(c => EF.Property<Guid>(c, "Id") == collectionId, ct);
            var account = await LoadAccountAsync(Read<Guid>(collection, "AccountId"), track: true, ct);

            // The corrections first, then the annulments: a refusal at the annulment, which is where the
            // database checks the window and the role, rolls the corrections back with it.
            foreach (var correction in preview.Corrections)
            {
                account.Append(correction);
            }

            await _context.SaveChangesAsync(ct);

            foreach (var annulment in preview.Annulments)
            {
                var recibo = rows.Single(r => Read<int>(r, "Number") == annulment.ReciboNumber);
                _context.ReciboAnnulments.Add(AnnulmentBag(Read<Guid>(recibo, "Id"), annulment.Reason));
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return VoidingResult.Done();
        }
        catch (Exception ex) when (VoidingRefusalOf(ex) is { } refusal)
        {
            return VoidingResult.Refused(refusal);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    /// <summary>What a payment is planned against: the account, its contract and the header the recibos will print.</summary>
    private sealed record Loaded(ContractAccount Account, Contract Contract, ReciboHeader Header);

    private async Task<ContractAccount> LoadAccountAsync(Guid accountId, bool track, CancellationToken ct)
    {
        IQueryable<ContractAccount> accounts = _context.ContractAccounts;
        if (!track)
        {
            accounts = accounts.AsNoTracking();
        }

        return await accounts.Include(a => a.Movements).SingleOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new InvalidOperationException($"No account exists with id '{accountId}'.");
    }

    private async Task<Loaded> LoadAsync(Guid accountId, bool track, CancellationToken ct)
    {
        var account = await LoadAccountAsync(accountId, track, ct);

        // The adjustments come with the contract: a period accrues at the canon in force when it fell due.
        IQueryable<Contract> contracts = _context.Contracts;
        if (!track)
        {
            contracts = contracts.AsNoTracking();
        }

        var contract = await contracts
            .Include(c => c.Adjustments)
            .Include(c => c.Parties)
            .Include(c => c.Units)
            .SingleAsync(c => c.Id == account.ContractId, ct);

        var partyIds = contract.Parties.Select(p => p.PartyId).ToList();
        var parties = await _context.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id)).ToListAsync(ct);
        var unitIds = contract.Units.Select(u => u.UnitId).ToList();
        var units = await _context.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToListAsync(ct);

        return new Loaded(account, contract, HeaderFor(contract, parties, units));
    }

    /// <summary>
    /// Materialises the periods due as of the received date into the loaded account, in memory, then plans.
    /// The same rule as <see cref="AccountMaterialiser"/> (rent, and the instalment that rides with it),
    /// without the save; a payment's commit writes what this appends.
    /// </summary>
    private CollectionPreview BuildPreview(Loaded loaded, CollectionRequest request, DateOnly today)
    {
        var receivedOn = request.ReceivedOn ?? today;

        // A future received date is refused by the planner; nothing is priced beyond today for it.
        foreach (var due in AccrualSchedule.PeriodsDue(loaded.Contract, receivedOn < today ? receivedOn : today))
        {
            if (loaded.Account.IsChargedFor(due.Period))
            {
                continue;
            }

            loaded.Account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), due.Canon, due.Period, due.DueDate));

            if (AccrualSchedule.InstalmentAmountFor(loaded.Contract, due.Period) is { } fee &&
                !loaded.Account.HasInstalmentFor(due.Period))
            {
                loaded.Account.Append(AccountMovement.ContractualFeeInstalment(Guid.NewGuid(), fee, due.Period, due.DueDate));
            }
        }

        return CollectionPlanner.Plan(loaded.Account, loaded.Contract, _terms, request, today, loaded.Header);
    }

    /// <summary>
    /// The design's issuance statement, verbatim, as an ADO command enlisted in the current EF transaction
    /// so that EF never wraps it in a sub-select and a rollback restores the counter. Null when the starting
    /// number was never set.
    /// </summary>
    private async Task<long?> TakeNextPairAsync(CancellationToken ct)
    {
        await using var command = _context.Database.GetDbConnection().CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText =
            """
            UPDATE recibo_numbering
               SET next_number = next_number + 2
             WHERE id = 1 AND next_number IS NOT NULL
            RETURNING next_number - 2 AS first_number
            """;

        return await command.ExecuteScalarAsync(ct) is { } first ? Convert.ToInt64(first) : null;
    }

    private void AddRecibo(Recibo recibo, Guid collectionId, Guid contractId)
    {
        var reciboId = Guid.NewGuid();
        _context.Recibos.Add(ReciboBag(recibo, reciboId, collectionId, contractId));

        foreach (var line in recibo.Lines)
        {
            _context.ReciboLines.Add(LineBag(line, reciboId));
        }
    }

    // ---- Errors: the only place a PostgresException is read ----

    private static CollectionRefusal? CommitRefusalOf(Exception exception) =>
        PostgresOf(exception) is { } pg ? DeskErrorMap.ForCollection(pg.SqlState, pg.ConstraintName) : null;

    private static VoidingRefusal? VoidingRefusalOf(Exception exception) =>
        PostgresOf(exception) is { } pg ? DeskErrorMap.ForVoiding(pg.SqlState, pg.ConstraintName) : null;

    private static PostgresException? PostgresOf(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg)
            {
                return pg;
            }
        }

        return null;
    }

    // ---- Header: the snapshot a recibo prints (design D12) ----

    private static ReciboHeader HeaderFor(Contract contract, IReadOnlyList<Party> parties, IReadOnlyList<Unit> units)
    {
        List<string> NamesIn(PartyRole role) => contract.Parties
            .Where(cp => cp.Role == role)
            .Select(cp => parties.Single(p => p.Id == cp.PartyId))
            .Select(p => $"{p.LastName}, {p.FirstName}")
            .Order(StringComparer.Ordinal)
            .ToList();

        var tenants = NamesIn(PartyRole.Tenant);
        var lessors = NamesIn(PartyRole.Lessor);

        if (tenants.Count == 0 || lessors.Count == 0 || units.Count == 0)
        {
            throw new InvalidOperationException(
                "A recibo needs a tenant, a lessor and a unit on the contract to print its header.");
        }

        return new ReciboHeader(
            string.Join(NameSeparator, tenants),
            lessors,
            string.Join(NameSeparator, units.Select(DescribeUnit).Order(StringComparer.Ordinal)));
    }

    private static string DescribeUnit(Unit unit)
    {
        var a = unit.Address;
        var floor = string.IsNullOrWhiteSpace(a.Floor) ? string.Empty : $" piso {a.Floor}";
        var apartment = string.IsNullOrWhiteSpace(a.Apartment) ? string.Empty : $" dpto {a.Apartment}";

        return $"{a.Street} {a.Number}{floor}{apartment}, {a.City}";
    }

    // ---- Property bags: the small typed helpers (tasks.md "Issues found" 9) ----

    private static T Read<T>(Dictionary<string, object> bag, string key) => (T)bag[key];

    private static Dictionary<string, object> CollectionBag(
        Guid id, Guid accountId, DateOnly period, int attempt, DateOnly receivedOn, Guid paymentId, Guid? recargoId) =>
        new()
        {
            ["Id"] = id,
            ["AccountId"] = accountId,
            ["Period"] = period,
            ["Attempt"] = checked((short)attempt),
            ["ReceivedOn"] = receivedOn,
            ["PaymentMovementId"] = paymentId,
            ["RecargoMovementId"] = recargoId!,
        };

    private static Dictionary<string, object> ReciboBag(Recibo recibo, Guid id, Guid collectionId, Guid contractId) =>
        new()
        {
            ["Id"] = id,
            ["CollectionId"] = collectionId,
            ["Number"] = checked((int)recibo.Number),
            ["Copy"] = recibo.Copy.ToString(),
            ["ContractId"] = contractId,
            ["Period"] = recibo.Period,
            ["ReceivedOn"] = recibo.ReceivedOn,
            ["TenantName"] = recibo.Header.Tenant,
            ["LessorNames"] = string.Join(NameSeparator, recibo.Header.Lessors),
            ["UnitsDescription"] = recibo.Header.Units,
            ["Legend"] = recibo.Legend,
            ["Total"] = recibo.Total,
        };

    private static Dictionary<string, object> LineBag(ReciboLine line, Guid reciboId) =>
        new()
        {
            ["Id"] = Guid.NewGuid(),
            ["ReciboId"] = reciboId,
            ["Position"] = checked((short)line.Position),
            ["Kind"] = line.Kind.ToString(),
            ["Amount"] = line.Amount,
        };

    private static Dictionary<string, object> AnnulmentBag(Guid reciboId, string reason) =>
        new()
        {
            ["Id"] = Guid.NewGuid(),
            ["ReciboId"] = reciboId,
            ["Reason"] = reason,
        };

    private static CollectionRecord CollectionFrom(Dictionary<string, object> bag, bool isAnnulled) =>
        new(
            Read<Guid>(bag, "Id"),
            Read<Guid>(bag, "AccountId"),
            Read<DateOnly>(bag, "Period"),
            Read<DateOnly>(bag, "ReceivedOn"),
            Read<Guid>(bag, "PaymentMovementId"),
            bag.TryGetValue("RecargoMovementId", out var recargo) && recargo is Guid recargoId ? recargoId : null,
            isAnnulled);

    private static Recibo ReciboFrom(Dictionary<string, object> row, IEnumerable<Dictionary<string, object>> lines)
    {
        var copy = Enum.Parse<ReciboCopy>(Read<string>(row, "Copy"));
        var header = new ReciboHeader(
            Read<string>(row, "TenantName"),
            Read<string>(row, "LessorNames").Split(NameSeparator),
            Read<string>(row, "UnitsDescription"));
        var draft = new ReciboDraft(
            copy,
            lines
                .OrderBy(l => Read<short>(l, "Position"))
                .Select(l => new ReciboLine(
                    Enum.Parse<ReciboLineKind>(Read<string>(l, "Kind")), Read<short>(l, "Position"), Read<decimal>(l, "Amount")))
                .ToList());

        return new Recibo(Read<int>(row, "Number"), Read<DateOnly>(row, "Period"), Read<DateOnly>(row, "ReceivedOn"), header, draft);
    }
}

/// <summary>
/// The database's refusals as the planners' own (design section 2), keyed by SQLSTATE and constraint name
/// and never by message text. Strings in, not exceptions, so the table can be tested without a database.
/// A pair that is not listed is null: the desk lets it surface as an error, because turning an unknown
/// failure into a refusal would hide a bug as an operator message.
/// </summary>
public static class DeskErrorMap
{
    public static CollectionRefusal? ForCollection(string? sqlState, string? constraintName) =>
        (sqlState, constraintName) switch
        {
            ("23505", "ux_collections_live_attempt" or "ux_account_movements_period_charge"
                or "ux_account_movements_instalment_period" or "ux_recibos_number") => CollectionRefusal.ConcurrentChange,
            ("40P01", _) => CollectionRefusal.ConcurrentChange,
            ("P0001", "collections_received_on_future") => CollectionRefusal.ReceivedOnInFuture,
            ("P0001", "collections_received_on_bound") => CollectionRefusal.ReceivedOnBeyondBound,
            ("P0001", "collections_attempt_rule") => CollectionRefusal.PeriodAlreadySettled,
            _ => null,
        };

    public static VoidingRefusal? ForVoiding(string? sqlState, string? constraintName) =>
        (sqlState, constraintName) switch
        {
            ("23505", "ux_recibo_annulments_recibo") => VoidingRefusal.AlreadyAnnulled,
            ("P0001", "recibo_annulments_admin_only") => VoidingRefusal.AdminOnly,
            ("P0001", "recibo_annulments_window_closed") => VoidingRefusal.WindowClosed,
            _ => null,
        };
}
