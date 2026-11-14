using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Login;

/// <summary>
/// Counts failed logins on this PC and makes the login window wait, so one person's typos never reach the count at which
/// Supabase's pooler locks the whole office out (limite-de-intentos-de-login, spec Requirements 2 to 4, design §3).
/// One gate serves the whole run of the application. It never sees a username: the count belongs to the PC.
/// </summary>
public sealed class LoginGate
{
    /// <summary>How many failed logins in a row start the wait (spec P2).</summary>
    public const int FailuresBeforeWait = 3;

    /// <summary>
    /// How long both waits last (spec P2, Decision 10): the pooler remembers failures for 150 seconds, so after this
    /// long every earlier failure has dropped out of its count.
    /// </summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(150);

    private const string LogContext = "Login";

    private readonly TimeProvider _time;
    private readonly IErrorLog _log;

    private int _failuresInARow;
    private DateTimeOffset _waitEndsAt = DateTimeOffset.MinValue;
    private string? _message;

    public LoginGate(TimeProvider time, IErrorLog log)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        _time = time;
        _log = log;
    }

    /// <summary>True while the login window must not attempt a login.</summary>
    public bool IsWaiting => _time.GetUtcNow() < _waitEndsAt;

    /// <summary>What is left of the wait; zero when there is none.</summary>
    public TimeSpan TimeLeft => IsWaiting ? _waitEndsAt - _time.GetUtcNow() : TimeSpan.Zero;

    /// <summary>The line under the fields, or null. A wait's message goes away when the wait ends.</summary>
    public string? Message => _message is not null && IsWaitMessage(_message) && !IsWaiting ? null : _message;

    /// <summary>The title above the message during a wait, or null.</summary>
    public string? WaitTitle => IsWaiting ? LoginTexts.WaitTitle : null;

    /// <summary>
    /// Runs one login attempt, unless a wait is on: then nothing reaches the database and the result is null
    /// (spec Decision 12). Otherwise the outcome is counted, and returned so the caller can act on a success.
    /// </summary>
    public async Task<LoginOutcome?> TryLogInAsync(Func<Task<LoginOutcome>> attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (IsWaiting)
        {
            return null;
        }

        var outcome = await attempt();
        Count(outcome);
        return outcome;
    }

    private void Count(LoginOutcome outcome)
    {
        switch (outcome)
        {
            case LoginOutcome.Accepted:
                _failuresInARow = 0;
                _message = null;
                break;

            case LoginOutcome.NotProvisioned:
                _failuresInARow = 0;
                _message = LoginTexts.NotProvisioned;
                break;

            case LoginOutcome.Rejected:
                _failuresInARow++;
                if (_failuresInARow < FailuresBeforeWait)
                {
                    _message = LoginTexts.Rejected;
                    break;
                }

                StartWait(LoginTexts.TooManyFailures);
                break;

            case LoginOutcome.Blocked blocked:
                _log.Record(LogContext, blocked.Cause);
                StartWait(LoginTexts.Blocked);
                break;

            case LoginOutcome.NoVerdict noVerdict:
                // Neither counts nor starts the count again: the pooler never judged it (spec Decision 8).
                _log.Record(LogContext, noVerdict.Cause);
                _message = LoginTexts.NoVerdict;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A login outcome the gate does not know.");
        }
    }

    private void StartWait(string message)
    {
        _failuresInARow = 0;
        _waitEndsAt = _time.GetUtcNow() + Wait;
        _message = message;
    }

    private static bool IsWaitMessage(string message) =>
        ReferenceEquals(message, LoginTexts.TooManyFailures) || ReferenceEquals(message, LoginTexts.Blocked);
}
