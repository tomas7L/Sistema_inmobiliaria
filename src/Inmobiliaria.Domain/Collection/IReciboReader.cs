namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// Reads issued recibos back exactly as they were issued (ui-de-cobranza spec Requirement 13, design section
/// 5.2): by either printed number, by collection, or the most recent collections. Nothing is recomputed from
/// the clock or the contract. The annulment is read at the moment of the call (Finding F3), so a pair voided
/// since an earlier read reads as annulled on the next.
/// </summary>
public interface IReciboReader
{
    /// <summary>The payment whose locatario or propietario recibo carries <paramref name="reciboNumber"/>, or null.</summary>
    Task<IssuedPayment?> FindByNumberAsync(long reciboNumber, CancellationToken ct = default);

    /// <summary>The payment recorded as <paramref name="collectionId"/>, or null.</summary>
    Task<IssuedPayment?> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default);

    /// <summary>The newest <paramref name="limit"/> collections, newest first.</summary>
    Task<IReadOnlyList<RecentCollection>> RecentAsync(int limit, CancellationToken ct = default);
}

/// <summary>One issued recibo with the id a voiding needs, which the domain <see cref="Recibo"/> does not carry.</summary>
public sealed record IssuedRecibo(Guid ReciboId, Recibo Recibo);

/// <summary>Who voided a pair, when and why. <paramref name="AnnulledBy"/> is the user's display name.</summary>
public sealed record AnnulmentRecord(string Reason, string AnnulledBy, DateTimeOffset AnnulledAt);

/// <summary>
/// One payment and its two recibos. <paramref name="RecordedOn"/> is the agency's date of
/// <paramref name="RecordedAt"/>; both times are in the agency's zone.
/// </summary>
public sealed record IssuedPayment(
    Guid CollectionId,
    DateOnly RecordedOn,
    DateTimeOffset RecordedAt,
    IssuedRecibo Locatario,
    IssuedRecibo Propietario,
    AnnulmentRecord? Annulment)
{
    public bool IsAnnulled => Annulment is not null;
}

/// <summary>One line of the recent collections list.</summary>
public sealed record RecentCollection(
    Guid CollectionId,
    long LocatarioNumber,
    long PropietarioNumber,
    string Tenant,
    DateOnly Period,
    DateOnly ReceivedOn,
    DateTimeOffset RecordedAt,
    bool IsAnnulled);
