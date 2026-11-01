using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Presentation.Common;

/// <summary>
/// One plain-text file per day (<c>errores-yyyy-MM-dd.log</c>) in a local folder, appended to: the time,
/// the context and the whole exception with its inner exceptions and stack. Any password-looking value is
/// masked before it is written (<see cref="SecretMask"/>), since a connection error can quote its connection
/// string and a failed password statement can quote the statement. Every failure to write is swallowed.
/// </summary>
public sealed class FileErrorLog : IErrorLog
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
                        + SecretMask.Mask(exception.ToString())
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
}
