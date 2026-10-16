using System.Data.Common;
using System.Text.RegularExpressions;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>An agency calendar day that does not move, for the tests that need a period "begun, not yet due".</summary>
internal sealed class FixedClock : IAgencyClock
{
    public FixedClock(DateOnly today)
    {
        Today = today;
    }

    public DateOnly Today { get; }
}

/// <summary>What a test forces to go wrong. Distinct from every database error so a test can tell them apart.</summary>
internal sealed class InjectedFailureException : Exception
{
    public InjectedFailureException(string message)
        : base(message)
    {
    }

    /// <summary>True when <paramref name="exception"/> or anything it wraps is an injected failure.</summary>
    public static bool IsIn(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InjectedFailureException)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>What a failure hook saw: how many commands matched, and whether it actually threw.</summary>
internal sealed class FailureProbe
{
    public int Matches;

    public bool Fired { get; set; }
}

/// <summary>The tables written to, in order, by every INSERT a session sent through EF.</summary>
internal sealed class InsertJournal
{
    private static readonly Regex Insert = new(@"INSERT INTO ""?(?<table>\w+)""?", RegexOptions.Compiled);

    private readonly List<string> _tables = [];

    public IReadOnlyList<string> Tables
    {
        get
        {
            lock (_tables)
            {
                return [.. _tables];
            }
        }
    }

    public void Record(string commandText)
    {
        lock (_tables)
        {
            foreach (Match match in Insert.Matches(commandText))
            {
                _tables.Add(match.Groups["table"].Value);
            }
        }
    }
}

/// <summary>Holds each session at one point until all of them have arrived (two real sessions, one instant).</summary>
internal sealed class SessionBarrier
{
    private readonly Barrier _barrier;

    public SessionBarrier(int participants)
    {
        _barrier = new Barrier(participants);
    }

    public void Arrive()
    {
        if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("The other session never reached the barrier.");
        }
    }
}

/// <summary>
/// A test-only <see cref="DbCommandInterceptor"/> (design section 9). It looks at the text of every
/// command EF sends on a session and may record it, throw, or hold the session at a barrier. The raw
/// counter UPDATE is an ADO command and never passes through here, which is why the tests that need a
/// failure after the counter use the INSERT that follows it.
/// </summary>
internal sealed class CommandHook : DbCommandInterceptor
{
    public static readonly Regex InsertRecibos = new(@"INSERT INTO ""?recibos""?\s", RegexOptions.Compiled);
    public static readonly Regex InsertCollections = new(@"INSERT INTO ""?collections""?\s", RegexOptions.Compiled);
    public static readonly Regex LockAccount = new(@"pg_advisory_xact_lock", RegexOptions.Compiled);
    public static readonly Regex InsertMovements = new(@"INSERT INTO ""?account_movements""?\s", RegexOptions.Compiled);

    private readonly Action<string> _visit;

    private CommandHook(Action<string> visit)
    {
        _visit = visit;
    }

    /// <summary>Records every INSERT, in the order EF sends them.</summary>
    public static CommandHook Recording(InsertJournal journal) => new(journal.Record);

    /// <summary>Throws on the <paramref name="nth"/> command that matches <paramref name="pattern"/>.</summary>
    public static CommandHook FailOnNth(Regex pattern, int nth, FailureProbe probe) =>
        new(text =>
        {
            if (!pattern.IsMatch(text))
            {
                return;
            }

            if (Interlocked.Increment(ref probe.Matches) == nth)
            {
                probe.Fired = true;
                throw new InjectedFailureException($"Forced failure on match {nth} of {pattern}.");
            }
        });

    /// <summary>Holds this session at <paramref name="barrier"/> before the first command matching <paramref name="pattern"/>.</summary>
    public static CommandHook WaitAtFirst(Regex pattern, SessionBarrier barrier)
    {
        var arrived = 0;

        return new(text =>
        {
            if (pattern.IsMatch(text) && Interlocked.Exchange(ref arrived, 1) == 0)
            {
                barrier.Arrive();
            }
        });
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        _visit(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _visit(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        _visit(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _visit(command.CommandText);
        return ValueTask.FromResult(result);
    }
}

/// <summary>A row count of every table the desk writes, plus the counter. Value equality is the point.</summary>
internal sealed record LedgerCounts(
    long Movements, long Collections, long Recibos, long Lines, long Annulments, int? NextNumber);

/// <summary>
/// The database a <see cref="DeskWorld"/> works on: a superuser context, and whatever must be released
/// with it. Either an isolated database of its own (dropped on dispose), or a database somebody else
/// prepared and owns, reached by connection string (only the context is released).
/// </summary>
internal sealed class DeskDatabase : IAsyncDisposable
{
    private readonly IAsyncDisposable? _owner;

    public DeskDatabase(InmobiliariaDbContext context, IAsyncDisposable? owner = null)
    {
        Context = context;
        _owner = owner;
    }

    public InmobiliariaDbContext Context { get; }

    public ValueTask DisposeAsync() => _owner?.DisposeAsync() ?? Context.DisposeAsync();
}

/// <summary>
/// A freshly migrated database with an agency in it: one contract (tenant, lessor, unit, rent, rate)
/// and its empty account per call to <see cref="SeedAccountAsync"/>, and the recibo counter started.
/// Isolated because the counter, and "no recibo exists yet", belong to the whole database. Hands out
/// as many contexts as a test has sessions, each on its own connection.
/// </summary>
internal sealed class DeskWorld : IAsyncDisposable
{
    public const int StartingNumber = 31001;

    public static readonly RecargoTerms Terms = new(0.02m, 0);

    private readonly List<InmobiliariaDbContext> _contexts = [];

    private DeskWorld(DeskDatabase database)
    {
        Database = database;
    }

    public DeskDatabase Database { get; }

    /// <summary>The agency's day, from the clock the production desk uses.</summary>
    public static DateOnly Today => new AgencyClock().Today;

    /// <summary>The first day of the month three months back: its rent is long due whatever day the suite runs.</summary>
    public static DateOnly OldestPeriod => FirstOfMonth(Today).AddMonths(-3);

    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>
    /// A world over a database that already exists and is already migrated, reached by
    /// <paramref name="connectionString"/> as given (pooling untouched). The caller owns the data: it
    /// resets the tables and the recibo counter itself, as this creates and drops nothing.
    /// </summary>
    public static DeskWorld Over(string connectionString)
    {
        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DeskWorld(new DeskDatabase(new InmobiliariaDbContext(options)));
    }

#if !PV1
    public static async Task<DeskWorld> CreateAsync(PostgresFixture fixture, int? startingNumber = StartingNumber)
    {
        var isolated = await IsolatedDatabase.CreateAsync(fixture);
        var world = new DeskWorld(new DeskDatabase(isolated.Context, isolated));

        if (startingNumber is { } number)
        {
            await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
            await ReciboTestSupport.ExecAsync(
                connection, "UPDATE recibo_numbering SET starting_number = @n WHERE id = 1", ("n", number));
        }

        return world;
    }
#endif

    /// <summary>A superuser context on its own connection, with the given interceptors. Disposed with the world.</summary>
    public InmobiliariaDbContext NewContext(params IInterceptor[] interceptors) => NewContext(false, interceptors);

    /// <summary>
    /// As above; <paramref name="pooled"/> turns Npgsql pooling ON, which is what production runs with and
    /// which changes session timing enough to expose lock-order races the unpooled suite hides.
    /// </summary>
    public InmobiliariaDbContext NewContext(bool pooled, params IInterceptor[] interceptors)
    {
        var connection = Database.Context.Database.GetConnectionString()!;
        if (pooled)
        {
            connection = new Npgsql.NpgsqlConnectionStringBuilder(connection) { Pooling = true }.ConnectionString;
        }

        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql(connection)
            .AddInterceptors(interceptors)
            .Options;

        var context = new InmobiliariaDbContext(options);
        _contexts.Add(context);

        return context;
    }

    /// <summary>A desk over a new session (or over <paramref name="context"/>), on the real clock unless told otherwise.</summary>
    public CollectionDesk NewDesk(IAgencyClock? clock = null, InmobiliariaDbContext? context = null) =>
        new(context ?? NewContext(), clock ?? new AgencyClock(), Terms);

    /// <summary>A contract with a tenant, a lessor and a unit, and its empty account. Returns the account id.</summary>
    public async Task<Guid> SeedAccountAsync(
        DateOnly? start = null, int dueDay = Contract.DefaultDueDay, ContractualFeePlan? plan = null, decimal? rate = 8m)
    {
        await using var context = NewContext();

        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Bv. Belgrano", "123", "Sunchales", "Santa Fe", "2322"));
        var tenant = new Party(Guid.NewGuid(), "Maria", "Perez");
        var lessor = new Party(Guid.NewGuid(), "Juan", "Gomez");
        var from = start ?? OldestPeriod;

        var contract = new Contract(
            Guid.NewGuid(), from, from.AddYears(2).AddDays(-1), 450_000m, [new UnitShare(unit.Id, 100m)],
            rate, dueDay, null, plan);
        contract.AssignParty(tenant, PartyRole.Tenant);
        contract.AssignParty(lessor, PartyRole.Lessor);

        var account = new ContractAccount(Guid.NewGuid(), contract.Id);

        context.Units.Add(unit);
        context.Parties.AddRange(tenant, lessor);
        context.Contracts.Add(contract);
        context.ContractAccounts.Add(account);
        await context.SaveChangesAsync();

        return account.Id;
    }

    /// <summary>Writes the rent due so far, the way a read of the account would (lazy accrual).</summary>
    public async Task MaterialiseAsync(Guid accountId, DateOnly through)
    {
        await using var context = NewContext();
        await new AccountMaterialiser(context).MaterialiseDueAccrualsThroughAsync(accountId, through);
    }

    /// <summary>The preview the operator would confirm: first to learn the figure, then handing exactly that.</summary>
    public static async Task<(CollectionPreview Preview, CollectionRequest Request)> ConfirmedPreviewAsync(
        ICollectionDesk desk, Guid accountId, DateOnly? period = null)
    {
        var probe = await desk.PreviewAsync(new CollectionRequest(accountId, period, null, 0m));
        var request = new CollectionRequest(accountId, period, null, probe.LedgerFigure);
        var preview = await desk.PreviewAsync(request);

        Assert.True(preview.CanCommit, $"The preview was refused: {preview.Refusal}.");

        return (preview, request);
    }

    public static async Task<CollectionResult> PayAsync(ICollectionDesk desk, Guid accountId, DateOnly? period = null)
    {
        var (preview, request) = await ConfirmedPreviewAsync(desk, accountId, period);
        return await desk.CommitAsync(preview, request);
    }

    public async Task<LedgerCounts> CountsAsync()
    {
        await using var connection = await ReciboTestSupport.OpenAsync(Database.Context);

        Task<long> Count(string table) =>
            ReciboTestSupport.ScalarAsync<long>(connection, $"SELECT count(*) FROM {table}");

        var (_, next) = await ReciboTestSupport.ReadCounterAsync(connection);

        return new LedgerCounts(
            await Count("account_movements"),
            await Count("collections"),
            await Count("recibos"),
            await Count("recibo_lines"),
            await Count("recibo_annulments"),
            next);
    }

    public async Task<long> CountAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await ReciboTestSupport.OpenAsync(Database.Context);
        return await ReciboTestSupport.ScalarAsync<long>(connection, sql, parameters);
    }

    /// <summary>The id of one recibo of the account's most recent collection.</summary>
    public async Task<Guid> LatestReciboIdAsync(Guid accountId, string copy = "Locatario")
    {
        await using var connection = await ReciboTestSupport.OpenAsync(Database.Context);
        return await ReciboTestSupport.ScalarAsync<Guid>(
            connection,
            """
            SELECT r.id FROM recibos r JOIN collections c ON c.id = r.collection_id
            WHERE c.account_id = @account AND r.copy = @copy
            ORDER BY r.number DESC LIMIT 1
            """,
            ("account", accountId), ("copy", copy));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        await Database.DisposeAsync();
    }
}
