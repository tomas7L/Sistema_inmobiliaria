using CommunityToolkit.Mvvm.ComponentModel;

namespace Inmobiliaria.Presentation.Common;

/// <summary>What an operation is, which decides what its failure tells the operator.</summary>
public enum OperationKind
{
    /// <summary>A read or a preview: a failure wrote nothing.</summary>
    Read,

    /// <summary>A payment's commit: a failure may still have written it.</summary>
    Commit,

    /// <summary>A voiding: a failure may still have written it, and the operator is told so in its own words.</summary>
    Void,
}

/// <summary>
/// How a run ended. <see cref="Completed"/> means the operation returned (a refusal is a value and
/// completes); otherwise it failed with <see cref="ErrorMessage"/> or was cancelled (no message).
/// </summary>
public sealed record OperationOutcome<T>(T? Value, bool Completed, string? ErrorMessage)
{
    public bool IsCancelled => !Completed && ErrorMessage is null;
}

/// <summary>
/// The one generic error path of a screen (design D24, D22). Every screen operation runs through it:
/// an exception becomes a fixed Spanish message and nothing else, the busy flag is reset in
/// <c>finally</c> so the commands come back after a failure, and nothing is retried. It never touches the
/// screen's typed values. A refusal returned by a port is an ordinary value and is not its business.
/// </summary>
public sealed partial class OperationRunner : ObservableObject
{
    private readonly IErrorLog? _errorLog;

    /// <summary>A runner that records into <see cref="DefaultErrorLog"/> as it stands when a failure happens.</summary>
    public OperationRunner()
    {
    }

    /// <summary>A runner that records into <paramref name="errorLog"/> only.</summary>
    public OperationRunner(IErrorLog errorLog)
    {
        ArgumentNullException.ThrowIfNull(errorLog);
        _errorLog = errorLog;
    }

    /// <summary>
    /// The log every runner built without one records into. The application installs the file log once at
    /// startup; every screen builds its runner itself, so this saves threading it through each of them.
    /// </summary>
    public static IErrorLog DefaultErrorLog { get; set; } = NullErrorLog.Instance;

    /// <summary>
    /// True while any operation runs; every state-changing command's <c>CanExecute</c> reads it. Runs can
    /// overlap (a superseded preview still finishing), so it counts them rather than flipping per run.
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    private int _running;

    /// <summary>The message of the last failed run, or null. Cleared when the next run starts.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    public async Task<OperationOutcome<T>> RunAsync<T>(Func<Task<T>> operation, OperationKind kind = OperationKind.Read)
    {
        ArgumentNullException.ThrowIfNull(operation);

        ErrorMessage = null;
        _running++;
        IsBusy = true;
        try
        {
            var value = await operation();
            return new OperationOutcome<T>(value, Completed: true, ErrorMessage: null);
        }
        catch (OperationCanceledException cancelled) when (kind == OperationKind.Read)
        {
            // A superseded or abandoned read: not a failure, and nothing to tell the operator. A cancelled
            // commit falls through: it may still have reached the database (S1 review). A superseded read
            // that had failed carries that failure inside; it is recorded, still unseen.
            if (cancelled.InnerException is { } failure)
            {
                Record("Superseded read", failure);
            }

            return new OperationOutcome<T>(default, Completed: false, ErrorMessage: null);
        }
        catch (Exception exception)
        {
            // Never on the screen: the exception's text can carry a connection string or a database error
            // (spec Requirement 4). It goes to the error log only.
            Record(kind.ToString(), exception);
            var message = kind switch
            {
                OperationKind.Commit => ErrorMessages.CommitFailed,
                OperationKind.Void => ErrorMessages.VoidFailed,
                _ => ErrorMessages.Generic,
            };
            ErrorMessage = message;
            return new OperationOutcome<T>(default, Completed: false, ErrorMessage: message);
        }
        finally
        {
            _running--;
            IsBusy = _running > 0;
        }
    }

    private void Record(string context, Exception exception)
    {
        try
        {
            (_errorLog ?? DefaultErrorLog).Record(context, exception);
        }
        catch (Exception)
        {
            // A log that fails must not turn into a second failure.
        }
    }
}
