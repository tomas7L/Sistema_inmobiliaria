using Npgsql;

namespace Inmobiliaria.Pv1.Tests;

/// <summary>
/// Turns what the human supplies into an Npgsql connection string. Supabase's dashboard hands out a URI
/// (<c>postgresql://postgres.&lt;ref&gt;:&lt;password&gt;@aws-0-xx.pooler.supabase.com:5432/postgres</c>), which
/// Npgsql does not read; an Npgsql keyword string (<c>Host=...;Username=...</c>) is used exactly as given.
///
/// Nothing here, and no message it produces, ever contains the password.
/// </summary>
internal static class Pv1ConnectionString
{
    private const int TransactionPoolerPort = 6543;

    public static string Normalize(string supplied)
    {
        var text = supplied.Trim();

        if (!text.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            return Checked(new NpgsqlConnectionStringBuilder(text));
        }

        if (text.Contains("[YOUR-PASSWORD]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[PASSWORD]", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The connection string still contains the dashboard's password placeholder; put the real password there.");
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                "The connection URI could not be read. A password with @ : / ? # or % in it must be percent-encoded.");
        }

        var separator = uri.UserInfo.IndexOf(':');
        var user = separator < 0 ? uri.UserInfo : uri.UserInfo[..separator];
        var password = separator < 0 ? string.Empty : uri.UserInfo[(separator + 1)..];

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(user),
            Password = Uri.UnescapeDataString(password),

            // Supabase only speaks TLS. A URI that says otherwise (sslmode=...) wins.
            SslMode = SslMode.Require,
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase) && parts.Length == 2)
            {
                builder.SslMode = ParseSslMode(Uri.UnescapeDataString(parts[1]));
            }
        }

        return Checked(builder);
    }

    /// <summary>
    /// libpq spells the modes with hyphens (<c>verify-ca</c>, <c>verify-full</c>); Npgsql's enum does not.
    /// </summary>
    private static SslMode ParseSslMode(string value)
    {
        if (Enum.TryParse<SslMode>(value.Replace("-", string.Empty), ignoreCase: true, out var mode) &&
            Enum.IsDefined(mode))
        {
            return mode;
        }

        throw new ArgumentException(
            $"Unknown sslmode '{value}'. Expected disable, allow, prefer, require, verify-ca or verify-full.");
    }

    /// <summary>Host, port and database only: safe to print.</summary>
    public static string Describe(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Host}:{builder.Port}/{builder.Database}";
    }

    private static string Checked(NpgsqlConnectionStringBuilder builder)
    {
        if (builder.Port == TransactionPoolerPort)
        {
            throw new InvalidOperationException(
                "Port 6543 is Supavisor's transaction pooler. PV1 is about the session pooler (port 5432).");
        }

        return builder.ConnectionString;
    }
}
