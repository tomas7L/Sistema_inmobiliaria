using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// The four helpers the linked <c>CollectionDeskFixture.cs</c> and <c>CollectionDeskConcurrencyTests.cs</c>
/// call on the class of the same name in Inmobiliaria.Infrastructure.Tests (ReciboSchemaTests.cs, which
/// this project cannot compile without dragging that suite along). Same signatures, same behaviour.
/// </summary>
internal static class ReciboTestSupport
{
    public static async Task<NpgsqlConnection> OpenAsync(InmobiliariaDbContext context)
    {
        var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    public static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<(int? Starting, int? Next)> ReadCounterAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT starting_number, next_number FROM recibo_numbering WHERE id = 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (
            reader.IsDBNull(0) ? null : reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }
}
