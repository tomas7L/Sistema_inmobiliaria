using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The agency's calendar day in Buenos Aires time. Touches no database: the instant comes from a
/// <see cref="TimeProvider"/> (the system one by default, a fixed one in tests) and is converted to
/// the agency's zone, so "today" turns over at 03:00 UTC and not at midnight UTC.
///
/// Argentina has no daylight saving, so the offset is a constant -03:00 today, but the conversion is
/// done through the zone anyway rather than by subtracting three hours.
/// </summary>
public sealed class AgencyClock : IAgencyClock
{
    /// <summary>IANA id, resolved on Linux and, with ICU present, on Windows.</summary>
    private const string IanaZoneId = "America/Argentina/Buenos_Aires";

    /// <summary>Windows registry id, the fallback when the IANA id is not known (pending verification PV4).</summary>
    private const string WindowsZoneId = "Argentina Standard Time";

    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;

    public AgencyClock(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _zone = ResolveZone();
    }

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime);

    /// <summary>
    /// The Buenos Aires zone: the IANA id first, the Windows id if that one throws.
    /// </summary>
    public static TimeZoneInfo ResolveZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IanaZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsZoneId);
        }
    }
}
