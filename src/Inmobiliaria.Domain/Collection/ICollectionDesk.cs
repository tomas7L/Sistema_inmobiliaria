using Inmobiliaria.Domain.Access;

namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// How a commit ended: either it wrote a collection and its two recibos, or it refused and wrote
/// nothing. A refusal is never an exception; anything the desk cannot map to one surfaces as an error.
/// </summary>
/// <param name="Refusal">Why the commit wrote nothing, or null when it succeeded.</param>
/// <param name="CollectionId">The collection that was written, or null on a refusal.</param>
/// <param name="LocatarioNumber">The number of the locatario recibo, or null on a refusal.</param>
/// <param name="PropietarioNumber">The number of the propietario recibo (always the locatario's plus one), or null on a refusal.</param>
public sealed record CollectionResult(
    CollectionRefusal? Refusal,
    Guid? CollectionId,
    long? LocatarioNumber,
    long? PropietarioNumber)
{
    public bool Succeeded => Refusal is null;

    public static CollectionResult Refused(CollectionRefusal refusal) => new(refusal, null, null, null);

    public static CollectionResult Committed(Guid collectionId, long locatarioNumber) =>
        new(null, collectionId, locatarioNumber, locatarioNumber + 1);
}

/// <summary>How a voiding ended: it annulled both recibos and reversed the payment, or it refused and wrote nothing.</summary>
/// <param name="Refusal">Why the voiding wrote nothing, or null when it succeeded.</param>
public sealed record VoidingResult(VoidingRefusal? Refusal)
{
    public bool Succeeded => Refusal is null;

    public static VoidingResult Refused(VoidingRefusal refusal) => new(refusal);

    public static VoidingResult Done() => new((VoidingRefusal?)null);
}

/// <summary>
/// The transaction of collecting a tenant payment and of voiding one (design section 2 and 7.5). The
/// pure planners decide what to write; the desk reads the account, writes it atomically and maps the
/// database's refusals back to the planners' own. A port, so the domain never learns what a database is
/// (design D23).
/// </summary>
public interface ICollectionDesk
{
    /// <summary>The preview of a payment. Writes nothing, not even the accruals it materialises in memory.</summary>
    Task<CollectionPreview> PreviewAsync(CollectionRequest request, CancellationToken ct = default);

    /// <summary>Commits <paramref name="preview"/> in one transaction, or refuses when a fresh preview differs from it.</summary>
    Task<CollectionResult> CommitAsync(
        CollectionPreview preview, CollectionRequest request, CancellationToken ct = default);

    /// <summary>
    /// The preview of voiding the payment that <paramref name="reciboId"/> belongs to (either copy
    /// names the pair). Reads when the payment was recorded from the database, never from the caller.
    /// </summary>
    Task<VoidingPreview> PreviewVoidAsync(
        Guid reciboId, string reason, UserRole actingRole, CancellationToken ct = default);

    /// <summary>Appends the corrections and both annulments in one transaction.</summary>
    Task<VoidingResult> VoidAsync(VoidingPreview preview, CancellationToken ct = default);
}
