using Inmobiliaria.Presentation.Login;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// The count of failed logins on one PC and the wait it starts (limite-de-intentos-de-login, spec Requirements 2 to 4,
/// Tests 4 to 12). The gate never sees a username, so the tests drive it through the outcome each attempt returns.
/// </summary>
public sealed class LoginGateTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingErrorLog _log = new();
    private int _attempts;

    private LoginGate NewGate() => new(_clock, _log);

    private Task<LoginOutcome?> Try(LoginGate gate, LoginOutcome outcome) =>
        gate.TryLogInAsync(() =>
        {
            _attempts++;
            return Task.FromResult(outcome);
        });

    private static LoginOutcome Rejected => new LoginOutcome.Rejected();

    // Test 4
    [Fact]
    public async Task TwoFailedLogins_LeaveTheButtonEnabled_AndTheThirdStartsTheWait()
    {
        var gate = NewGate();

        await Try(gate, Rejected);
        await Try(gate, Rejected);

        Assert.False(gate.IsWaiting);
        Assert.Equal(LoginTexts.Rejected, gate.Message);
        Assert.Null(gate.WaitTitle);

        await Try(gate, Rejected);

        Assert.True(gate.IsWaiting);
        Assert.Equal(LoginTexts.TooManyFailures, gate.Message);
        Assert.Equal(LoginTexts.WaitTitle, gate.WaitTitle);
    }

    // Test 5
    [Fact]
    public async Task TheWait_EndsExactly150SecondsAfterTheThirdFailure()
    {
        var gate = NewGate();
        await Try(gate, Rejected);
        await Try(gate, Rejected);
        await Try(gate, Rejected);

        Assert.Equal(TimeSpan.FromSeconds(150), LoginGate.Wait);
        Assert.Equal("Espere 2:30", LoginTexts.Countdown(gate.TimeLeft));

        _clock.Advance(TimeSpan.FromSeconds(149));
        Assert.True(gate.IsWaiting);
        Assert.Equal("Espere 0:01", LoginTexts.Countdown(gate.TimeLeft));

        _clock.Advance(OneSecond);
        Assert.False(gate.IsWaiting);
        Assert.Equal(TimeSpan.Zero, gate.TimeLeft);
        Assert.Null(gate.Message);
        Assert.Null(gate.WaitTitle);
    }

    // Test 6
    [Fact]
    public async Task AfterTheWait_OneFailedLogin_DoesNotStartAnotherWait()
    {
        var gate = NewGate();
        await Try(gate, Rejected);
        await Try(gate, Rejected);
        await Try(gate, Rejected);
        _clock.Advance(LoginGate.Wait);

        await Try(gate, Rejected);

        Assert.False(gate.IsWaiting);
        Assert.Equal(LoginTexts.Rejected, gate.Message);
    }

    // Test 7: the attempts below stand for "maria", "maria" and "jose"; the gate counts them all the same.
    [Fact]
    public async Task AThirdFailure_UnderAnotherUsername_StartsTheWait()
    {
        var gate = NewGate();
        var refusedFor = new List<string>();

        foreach (var username in new[] { "maria", "maria", "jose" })
        {
            await gate.TryLogInAsync(() =>
            {
                refusedFor.Add(username);
                return Task.FromResult<LoginOutcome>(new LoginOutcome.Rejected());
            });
        }

        Assert.Equal(["maria", "maria", "jose"], refusedFor);
        Assert.True(gate.IsWaiting);
    }

    // Test 8
    [Fact]
    public async Task ALoginWithNoVerdict_NeitherCounts_NorStartsTheCountAgain()
    {
        var cause = new TimeoutException("no network");
        var gate = NewGate();
        await Try(gate, Rejected);
        await Try(gate, Rejected);

        await Try(gate, new LoginOutcome.NoVerdict(cause));

        Assert.False(gate.IsWaiting);
        Assert.Equal(LoginTexts.NoVerdict, gate.Message);
        Assert.Equal(("Login", (Exception)cause), Assert.Single(_log.Entries));

        await Try(gate, Rejected);

        Assert.True(gate.IsWaiting);
    }

    // Test 9
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALoginTheDatabaseAccepted_StartsTheCountAgain(bool provisioned)
    {
        var gate = NewGate();
        await Try(gate, Rejected);
        await Try(gate, Rejected);

        await Try(gate, provisioned ? new LoginOutcome.Accepted() : new LoginOutcome.NotProvisioned());
        Assert.Equal(provisioned ? null : LoginTexts.NotProvisioned, gate.Message);

        await Try(gate, Rejected);

        Assert.False(gate.IsWaiting);
        Assert.Equal(LoginTexts.Rejected, gate.Message);
    }

    // Test 10, and spec Decision 13: the block is logged.
    [Fact]
    public async Task TheBlock_StartsTheWaitAtOnce_WithNoFailuresBefore_AndIsLogged()
    {
        var cause = new InvalidOperationException("(ECIRCUITBREAKER) too many authentication failures");
        var gate = NewGate();

        await Try(gate, new LoginOutcome.Blocked(cause));

        Assert.True(gate.IsWaiting);
        Assert.Equal(LoginGate.Wait, gate.TimeLeft);
        Assert.Equal(LoginTexts.Blocked, gate.Message);
        Assert.Equal(LoginTexts.WaitTitle, gate.WaitTitle);
        Assert.Equal(("Login", (Exception)cause), Assert.Single(_log.Entries));

        _clock.Advance(LoginGate.Wait);
        Assert.False(gate.IsWaiting);
    }

    // Test 11
    [Fact]
    public async Task DuringAWait_LoggingIn_ReachesTheAuthenticatorZeroTimes()
    {
        var gate = NewGate();
        await Try(gate, Rejected);
        await Try(gate, Rejected);
        await Try(gate, Rejected);
        Assert.Equal(3, _attempts);

        var outcome = await Try(gate, new LoginOutcome.Accepted());
        _clock.Advance(LoginGate.Wait - OneSecond);
        var later = await Try(gate, new LoginOutcome.Accepted());

        Assert.Null(outcome);
        Assert.Null(later);
        Assert.Equal(3, _attempts);
        Assert.Equal(LoginTexts.TooManyFailures, gate.Message);
    }

    // Test 12
    [Fact]
    public async Task TheWaitMessages_AreTheirOwn_AndTheSameForAnyUsername()
    {
        string[] texts = [LoginTexts.TooManyFailures, LoginTexts.Blocked, LoginTexts.Rejected, LoginTexts.NoVerdict];
        Assert.Equal(texts.Length, texts.Distinct().Count());

        var forAnExistingUser = NewGate();
        var forAnUnknownUser = NewGate();
        foreach (var gate in new[] { forAnExistingUser, forAnUnknownUser })
        {
            await Try(gate, Rejected);
            await Try(gate, Rejected);
            await Try(gate, Rejected);
        }

        Assert.Equal(forAnExistingUser.Message, forAnUnknownUser.Message);
        Assert.Equal(forAnExistingUser.WaitTitle, forAnUnknownUser.WaitTitle);
    }

    [Fact]
    public void TheTexts_AreMateosApprovedWording_WithTheWaitWrittenFromTheConstant()
    {
        Assert.Equal("Demasiados intentos fallidos", LoginTexts.WaitTitle);
        Assert.Equal(
            "Hubo demasiados intentos fallidos. Espere 2:30 antes de volver a intentar. Si intenta antes, la espera se alarga.",
            LoginTexts.TooManyFailures);
        Assert.Equal(
            "Hubo demasiados intentos fallidos y el ingreso quedó en pausa. Espere 2:30 antes de volver a intentar. Si intenta antes, la espera se alarga.",
            LoginTexts.Blocked);
        Assert.Equal("Usuario o contraseña incorrectos.", LoginTexts.Rejected);
        Assert.Equal("Esta cuenta no puede iniciar sesión. Consulte al administrador.", LoginTexts.NotProvisioned);
        Assert.Equal(
            "No se pudo conectar con el sistema. Revise la conexión a internet e intente de nuevo. Si sigue igual, avise al administrador.",
            LoginTexts.NoVerdict);
    }
}
