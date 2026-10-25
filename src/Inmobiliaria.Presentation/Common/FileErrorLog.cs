using System.Text.RegularExpressions;

namespace Inmobiliaria.Presentation.Common;

/// <summary>
/// One plain-text file per day (<c>errores-yyyy-MM-dd.log</c>) in a local folder, appended to: the time,
/// the context and the whole exception with its inner exceptions and stack. Any password-looking value is
/// masked before it is written, since a connection error can quote its connection string. Every failure to
/// write is swallowed.
/// </summary>
public sealed partial class FileErrorLog : IErrorLog
{
    private readonly string _folder;
    private readonly Func<DateTimeOffset> _now;
    private readonly Lock _gate = new();

    public FileErrorLog(string folder, Func<DateTimeOffset>? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        _folder = folder;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public void Record(string context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var at = _now();
            var entry = $"{at:yyyy-MM-dd HH:mm:ss zzz} [{context}]{Environment.NewLine}"
                        + Mask(exception.ToString())
                        + Environment.NewLine + Environment.NewLine;

            lock (_gate)
            {
                Directory.CreateDirectory(_folder);
                File.AppendAllText(Path.Combine(_folder, $"errores-{at:yyyy-MM-dd}.log"), entry);
            }
        }
        catch (Exception)
        {
            // Losing a record must never become a second failure on the operator's screen.
        }
    }

    private static string Mask(string text) => PasswordValue().Replace(text, "${key}=***");

    [GeneratedRegex(@"(?<key>password)\s*=\s*('[^']*'|""[^""]*""|[^;\s]*)", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordValue();
}
