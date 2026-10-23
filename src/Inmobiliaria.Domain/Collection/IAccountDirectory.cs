using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// How the cash desk finds the account to collect (ui-de-cobranza spec Requirement 6, design section 5.1):
/// by tenant, lessor, unit or the contract's short id, ignoring case and accents, every contract status
/// included. A search reads and never writes; nothing is materialised.
///
/// In <c>Collection</c>, not <c>Accounts</c>: readiness is the cash desk's question, and the account model keeps
/// its rule that no property there is a boolean (<c>AmountOwedTests.NoBooleanFlag_CarriesFrozenState</c>); a
/// frozen account is still only a dated movement.
/// </summary>
public interface IAccountDirectory
{
    /// <summary>
    /// The accounts whose tenant, lessor, unit or short id contain <paramref name="text"/>, sorted by tenant,
    /// at most fifty. Fewer than two meaningful characters find nothing.
    /// </summary>
    Task<IReadOnlyList<AccountListing>> SearchAsync(string text, CancellationToken ct = default);
}

/// <summary>One account the desk can open.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="ContractId">Its contract.</param>
/// <param name="ContractShortId">The first eight hex characters of the contract id, the only handle an operator has for it (design D18).</param>
/// <param name="Tenants">The tenant's name, "Last, First", as the recibo prints it.</param>
/// <param name="Lessors">The lessors' names, as the recibo prints them.</param>
/// <param name="Units">The units, as the recibo describes them.</param>
/// <param name="Status">The contract's status; an ended contract can still owe (spec Decision 29).</param>
/// <param name="CutoverDate">The contract's cutover date, information only (spec Decision 21).</param>
/// <param name="Readiness">Whether the desk can collect on it, and why not.</param>
public sealed record AccountListing(
    Guid AccountId,
    Guid ContractId,
    string ContractShortId,
    string Tenants,
    string Lessors,
    string Units,
    ContractStatus Status,
    DateOnly? CutoverDate,
    AccountReadiness Readiness);

/// <summary>
/// The three hard gates of spec Decision 21. A missing cutover date is not one of them, and a 0% rate is
/// entered.
/// </summary>
public sealed record AccountReadiness(bool RateEntered, bool Frozen, bool StartingNumberSet)
{
    public bool IsReady => RateEntered && !Frozen && StartingNumberSet;
}
