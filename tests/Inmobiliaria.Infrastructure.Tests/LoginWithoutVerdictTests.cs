using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Inmobiliaria.Infrastructure.Access;
using Npgsql;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// A login the database never judged, and the pooler's own refusal, which plain PostgreSQL never sends, so
/// the Testcontainers login tests cannot reach them (live smoke H.2 of gestion-de-usuarios). A loopback socket
/// plays the pooler and replies with the exact error it sent.
/// </summary>
public sealed class LoginWithoutVerdictTests
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
    public async Task OtherPoolerErrors_AreUnavailable_NeverARejection(string message)
    {
        var result = await LogInAgainstPoolerReplying(message);

        var unavailable = Assert.IsType<AuthenticationResult.Unavailable>(result);
        Assert.Equal(message, Assert.IsType<PostgresException>(unavailable.Cause).MessageText);
    }

    /// <summary>Nothing listens on the port, as when the office has no network: the login window must survive it.</summary>
    [Fact]
    public async Task NoServerAnswering_IsUnavailable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var result = await AuthenticatorOn(port).AuthenticateAsync("maria", "any-password");

        Assert.IsType<NpgsqlException>(Assert.IsType<AuthenticationResult.Unavailable>(result).Cause, exactMatch: false);
    }

    /// <summary>The server is reached and then the line drops before it answers.</summary>
    [Fact]
    public async Task ConnectionDroppedBeforeAnswering_IsUnavailable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var dropper = DropEveryConnectionAsync(listener, timeout.Token);

        var result = await AuthenticatorOn(port).AuthenticateAsync("maria", "any-password", timeout.Token);

        await timeout.CancelAsync();
        await dropper;
        Assert.IsType<AuthenticationResult.Unavailable>(result);
    }

    private static NpgsqlAuthenticator AuthenticatorOn(int port) =>
        new(new ConnectionEndpoint("127.0.0.1", port, "postgres", "testproject", SslMode.Disable));

    private static async Task DropEveryConnectionAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The login finished, so nothing else will connect.
        }
    }

    private static async Task<AuthenticationResult> LogInAgainstPoolerReplying(string message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var pooler = ServeFatalErrorsAsync(listener, message, timeout.Token);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            return await AuthenticatorOn(port).AuthenticateAsync("noexiste", "any-password", timeout.Token);
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
