namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// The fact that one recibo was annulled, and why. It names the recibo by its number, which is unique
/// and never reused. Who annulled it and when are stamped by the database, never carried here
/// (design D10). A recibo is never edited or deleted: an annulment is appended beside it.
/// </summary>
public sealed record ReciboAnnulment
{
    public long ReciboNumber { get; }

    public string Reason { get; }

    public ReciboAnnulment(long reciboNumber, string reason)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(reciboNumber, 1L);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ReciboNumber = reciboNumber;
        Reason = reason.Trim();
    }
}
