using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Pv1.Tests;

/// <summary>
/// The one database PV1 runs on, and the rule that it may only ever touch a database it prepared itself.
///
/// The guard, in order:
/// <list type="number">
///   <item><description>Table <c>pv1_scratch_marker</c> exists: PV1 made this database. Carry on.</description></item>
///   <item><description>No marker, and the public schema is completely empty (no tables, no migrations
///   history): a brand-new project. Create the marker first, then migrate.</description></item>
///   <item><description>Anything else, which is what production looks like (migrations, no marker):
///   fail, loudly, having written nothing.</description></item>
/// </list>
/// Every reset re-checks the marker before it truncates anything.
/// </summary>
public sealed class Pv1Fixture : IAsyncLifetime
{
    public const string VariableName = "INMOBILIARIA_PV1_DB";
    public const string MarkerTable = "pv1_scratch_marker";

    // What the three tests write, and everything that hangs off it. The ledger tables refuse UPDATE and
    // DELETE row by row; TRUNCATE is not a row operation, so the triggers do not stand in the way.
    private const string TruncateSql =
        """
        TRUNCATE recibo_annulments, recibo_lines, recibos, collections, account_movements,
                 contract_accounts, contract_parties, contract_units, contracts, parties, units
        CASCADE
        """;

    public string? ConnectionString { get; private set; }

    public bool IsConfigured => ConnectionString is not null;

    public string SkipReason => $"{VariableName} not set — PV1 not run.";

    public async Task InitializeAsync()
    {
        var supplied = Environment.GetEnvironmentVariable(VariableName);
        if (string.IsNullOrWhiteSpace(supplied))
        {
            return;
        }

        ConnectionString = Pv1ConnectionString.Normalize(supplied);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        if (!await MarkerExistsAsync(connection))
        {
            var migrated = await ScalarAsync<bool>(
                connection, "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL");
            var objects = await ScalarAsync<long>(
                connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'");

            if (migrated || objects > 0)
            {
                throw Refusal(
                    $"It has {objects} table(s) in the public schema and {(migrated ? "a" : "no")} migrations history, and no {MarkerTable}.");
            }

            // The marker goes in before the migrations, so a run that dies half way is recognised, not refused.
            await ExecAsync(
                connection,
                $"""
                CREATE TABLE public.{MarkerTable} (
                    created_at timestamptz NOT NULL DEFAULT now(),
                    note text NOT NULL DEFAULT 'Created by tests/Inmobiliaria.Pv1.Tests. A throwaway database: PV1 truncates it.'
                );
                INSERT INTO public.{MarkerTable} DEFAULT VALUES;
                """);
        }

        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>().UseNpgsql(ConnectionString).Options;
        await using var context = new InmobiliariaDbContext(options);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Back to an empty agency with the recibo counter started at 31001. The counter can only be set when
    /// no recibo exists, hence the truncate first; it goes through NULL so the cutover trigger re-seeds
    /// next_number from the new starting number.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        if (!await MarkerExistsAsync(connection))
        {
            throw Refusal($"Table {MarkerTable} is missing; nothing was reset.");
        }

        await using var transaction = await connection.BeginTransactionAsync();

        await ExecAsync(connection, TruncateSql, transaction);
        await ExecAsync(connection, "UPDATE recibo_numbering SET starting_number = NULL WHERE id = 1", transaction);
        await ExecAsync(
            connection,
            $"UPDATE recibo_numbering SET starting_number = {Inmobiliaria.Infrastructure.Tests.DeskWorld.StartingNumber} WHERE id = 1",
            transaction);

        await transaction.CommitAsync();
    }

    private InvalidOperationException Refusal(string detail) =>
        new(
            "This database was not prepared by PV1; refusing to run "
            + $"(is {VariableName} pointing at production?) Target: {Pv1ConnectionString.Describe(ConnectionString!)}. {detail}");

    private static async Task<bool> MarkerExistsAsync(NpgsqlConnection connection) =>
        await ScalarAsync<bool>(connection, $"SELECT to_regclass('public.{MarkerTable}') IS NOT NULL");

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class Pv1Collection : ICollectionFixture<Pv1Fixture>
{
    public const string Name = "Pv1";
}
