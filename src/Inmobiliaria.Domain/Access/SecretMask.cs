using System.Text.RegularExpressions;

namespace Inmobiliaria.Domain.Access;

/// <summary>
/// Removes password values from text that is about to be written to a log (gestion-de-usuarios design
/// D10). Two forms: a connection-string assignment (<c>Password=...</c>) and the clause PostgreSQL quotes
/// back when a password statement fails inside a function (<c>PASSWORD '...'</c> or <c>PASSWORD E'...'</c>,
/// design Finding F5).
/// </summary>
public static partial class SecretMask
{
    public static string Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var masked = SqlPasswordClause().Replace(text, "${key} ***");
        return AssignedPassword().Replace(masked, "${key}=***");
    }

    /// <summary>
    /// As <see cref="Mask(string)"/>, after removing every occurrence of <paramref name="secret"/> itself, in any case and with or
    /// without its spaces: a bare app password matches neither pattern (notificaciones design D7, Finding F4).
    /// </summary>
    public static string Mask(string text, string secret)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(secret);

        foreach (var form in new[] { secret, secret.Replace(" ", string.Empty, StringComparison.Ordinal) }
                     .Where(f => f.Length > 0)
                     .Distinct(StringComparer.Ordinal))
        {
            text = text.Replace(form, "***", StringComparison.OrdinalIgnoreCase);
        }

        return Mask(text);
    }

    [GeneratedRegex(@"(?<key>password)\s*=\s*('[^']*'|""[^""]*""|[^;\s]*)", RegexOptions.IgnoreCase)]
    private static partial Regex AssignedPassword();

    // A SQL string literal: '' is an escaped quote; in an E'' literal a backslash escapes the next character. The
    // closing quote is optional, so a literal cut off by a truncated message is still masked, up to the line end.
    [GeneratedRegex(@"(?<key>\bpassword)\s+(E'(?:[^'\\\r\n]|\\.|'')*'?|'(?:[^'\r\n]|'')*'?)", RegexOptions.IgnoreCase)]
    private static partial Regex SqlPasswordClause();
}
