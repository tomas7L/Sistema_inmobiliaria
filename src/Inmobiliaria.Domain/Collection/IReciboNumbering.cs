namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// The go-live act of numbering: the figure the first recibo will carry (design D7). Issuing numbers
/// is not here; it happens inside the payment transaction, which is the only place a number can be
/// taken without the chance of a gap.
/// </summary>
public interface IReciboNumbering
{
    /// <summary>
    /// Sets the starting recibo number. The database lets only an Admin do it, and only while no recibo
    /// exists; either refusal surfaces as the database's own error.
    /// </summary>
    Task SetStartingNumberAsync(int startingNumber, CancellationToken ct = default);
}
