namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// The mora worklist, read-model side of this capability. Lives in Domain — a plain interface with
/// no EF Core, Npgsql or WPF reference — so the notifications change can depend on it without
/// reaching into this capability's internals. The only implementation is the Infrastructure
/// adapter of the same name.
///
/// This query exists because of how the surcharge behaves. Two percent a day, simple and
/// uncapped, doubles a debt in fifty days. Nobody intends to let that happen, and nobody notices
/// it either if the only way to find out is to open each account one at a time. Making the list
/// cheap to ask for is what turns an uncapped rule into a safe one.
///
/// This capability owes notifications the query and nothing more: it sends nothing and schedules
/// nothing.
/// </summary>
public interface IMoraWorklistQuery
{
    /// <summary>
    /// Every account in mora as of <paramref name="asOf"/>, oldest debt first.
    ///
    /// IT MATERIALISES BEFORE IT READS. This is the highest-ranked risk of the whole change: with
    /// no scheduler, a period exists only once something has asked about it, so a worklist that
    /// merely queried movements would report an account nobody has ever opened as perfectly
    /// clear. The tenant three months behind would be the one who never appears.
    /// </summary>
    /// <param name="terms">
    /// The surcharge terms to measure with — never a constant, and never defaulted. A contract
    /// carries its own due day but not its own rate, so the caller supplies what the agency's
    /// leases say. The day a contract does carry its rate, this signature does not change: the
    /// implementation reads it instead.
    /// </param>
    Task<IReadOnlyList<MoraAccount>> GetAccountsInMoraAsync(
        DateOnly asOf, RecargoTerms terms, CancellationToken ct = default);
}
