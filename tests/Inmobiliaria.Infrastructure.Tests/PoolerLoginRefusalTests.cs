using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Inmobiliaria.Infrastructure.Access;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// The Supabase pooler answers a login for an unknown or deactivated role with its own error, which plain
/// PostgreSQL never sends, so the Testcontainers login tests cannot reach it (live smoke H.2 of
/// gestion-de-usuarios). A loopback socket plays the pooler and replies with the exact error it sent.
/// </summary>
public sealed class PoolerLoginRefusalTests
{
    private const string InternalError = "XX000";

    /// <summary>Text captured from the pooler on 2026-11-04, for a deactivated user and for a nonexistent one.</summary>
    [Fact]
    public async Task PoolerUserNotFound_IsAnOrdinaryRejection()
    {
        var result = await LogInAgainstPoolerReplying("(EAUTHQUERY) user not found in the database");

        Assert.IsType<AuthenticationResult.Rejected>(result);
    }

    /// <summary>These mean a broken setup or role, not a mistyped login: calling them a wrong password would hide them.</summary>
    [Theory]
    [InlineData("Tenant or user not found")]
    [InlineData("(EAUTHQUERY) unsupported or invalid secret format")]
    public async Task OtherPoolerErrors_AreNotPassedOffAsARejection(string message)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => LogInAgainstPoolerReplying(message));

        Assert.Equal(message, exception.MessageText);
    }

    private static async Task<AuthenticationResult> LogInAgainstPoolerReplying(string message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var pooler = ServeFatalErrorsAsync(listener, message, timeout.Token);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var authenticator = new NpgsqlAuthenticator(
            new ConnectionEndpoint("127.0.0.1", port, "postgres", "testproject", SslMode.Disable));

        try
        {
            return await authenticator.AuthenticateAsync("noexiste", "any-password", timeout.Token);
        }
        finally
        {
            await timeout.CancelAsync();
            await pooler;
        }
    }

    /// <summary>Serves every connection, because Npgsql opens a second one to retry after the first refusal.</summary>
    private static async Task ServeFatalErrorsAsync(TcpListener listener, string message, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                await ReplyWithFatalErrorAsync(client.GetStream(), message, ct);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // The login finished, or the client hung up mid-handshake: the login's own result is what is asserted.
        }
    }

    private static async Task ReplyWithFatalErrorAsync(NetworkStream stream, string message, CancellationToken ct)
    {
        const int SslRequest = 80877103;
        const int GssEncRequest = 80877104;

        // Encryption requests are declined so the client goes on to its real startup message.
        while (true)
        {
            var header = new byte[8];
            await stream.ReadExactlyAsync(header, ct);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            var code = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
            await stream.ReadExactlyAsync(new byte[length - header.Length], ct);

            if (code is not (SslRequest or GssEncRequest))
            {
                break;
            }

            await stream.WriteAsync(new[] { (byte)'N' }, ct);
        }

        await stream.WriteAsync(FatalErrorResponse(message), ct);
    }

    private static byte[] FatalErrorResponse(string message)
    {
        var fields = Encoding.UTF8.GetBytes($"SFATAL\0VFATAL\0C{InternalError}\0M{message}\0\0");

        var response = new byte[1 + sizeof(int) + fields.Length];
        response[0] = (byte)'E';
        BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(1), sizeof(int) + fields.Length);
        fields.CopyTo(response, 1 + sizeof(int));
        return response;
    }
}
