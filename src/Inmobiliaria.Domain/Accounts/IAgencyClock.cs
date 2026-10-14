namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// The agency's calendar day. Rent falls due, recargo is counted and the back-date and voiding
/// windows are measured in the agency's own days (Sunchales, UTC-3), never the server's or the
/// database's. A port, so the domain never reads the system clock and tests fix the day
/// (cobranza-y-recibos design D16).
/// </summary>
public interface IAgencyClock
{
    /// <summary>The current date in the agency's time zone.</summary>
    DateOnly Today { get; }
}
