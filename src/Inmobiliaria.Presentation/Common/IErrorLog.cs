namespace Inmobiliaria.Presentation.Common;

/// <summary>
/// Where an unexpected failure is kept for diagnosis (live smoke H.5, 2026-10-24). The screen still shows
/// only the fixed text of <see cref="ErrorMessages"/>; this is the one place the exception itself goes.
/// An implementation may throw: callers swallow it, since losing a record must never stop an operation.
/// </summary>
public interface IErrorLog
{
    /// <summary>Keeps <paramref name="exception"/> with a short <paramref name="context"/> such as "Read" or "Commit".</summary>
    void Record(string context, Exception exception);
}

/// <summary>Keeps nothing. The default until the application installs a real log.</summary>
public sealed class NullErrorLog : IErrorLog
{
    public static readonly NullErrorLog Instance = new();

    private NullErrorLog()
    {
    }

    public void Record(string context, Exception exception)
    {
    }
}
