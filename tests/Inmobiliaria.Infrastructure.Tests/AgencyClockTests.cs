using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Infrastructure.Persistence;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// The agency's calendar day (cobranza-y-recibos design D16, pending verification PV4). No container
/// and no database: a fixed instant through an injectable time source, so the test is deterministic
/// on any machine. CI is Linux and the developer's machine is Windows, which is why the zone lookup
/// has a fallback id and why this must be run on both.
/// </summary>
public class AgencyClockTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DateOnly TodayAt(DateTimeOffset instant) =>
        new AgencyClock(new FixedTimeProvider(instant)).Today;

    [Fact]
    public void TheBuenosAiresZone_Resolves_OnThisMachine()
    {
        var zone = AgencyClock.ResolveZone();

        Assert.Equal(TimeSpan.FromHours(-3), zone.GetUtcOffset(new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc)));
    }

    // 23:30 in Buenos Aires on 14 October is already 15 October in UTC: the agency's day is still the 14th.
    [Fact]
    public void At2330InBuenosAires_TodayIsStillThatDay_EvenThoughUtcHasTurnedOver()
    {
        var instant = new DateTimeOffset(2026, 10, 14, 23, 30, 0, TimeSpan.FromHours(-3));

        Assert.Equal(new DateOnly(2026, 10, 15), DateOnly.FromDateTime(instant.UtcDateTime));
        Assert.Equal(new DateOnly(2026, 10, 14), TodayAt(instant));
    }

    [Fact]
    public void At0030InBuenosAires_TodayIsTheNewDay()
    {
        var instant = new DateTimeOffset(2026, 10, 15, 0, 30, 0, TimeSpan.FromHours(-3));

        Assert.Equal(new DateOnly(2026, 10, 15), TodayAt(instant));
    }

    [Fact]
    public void TheDayTurnsOverAtThreeUtc_NotAtMidnightUtc()
    {
        Assert.Equal(new DateOnly(2026, 10, 14), TodayAt(new DateTimeOffset(2026, 10, 15, 2, 59, 59, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 10, 15), TodayAt(new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void TheClock_IsThePortTheDomainDefines()
    {
        Assert.IsAssignableFrom<IAgencyClock>(new AgencyClock());
    }
}
