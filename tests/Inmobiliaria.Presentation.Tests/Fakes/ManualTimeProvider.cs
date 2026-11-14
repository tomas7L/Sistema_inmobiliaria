namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>A clock that moves only when the test moves it, so a wait is tested without sleeping.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 13, 19, 30, 0, TimeSpan.FromHours(-3));

    public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();

    public void Advance(TimeSpan by) => _now += by;
}
