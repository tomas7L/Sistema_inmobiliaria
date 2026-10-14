namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// The two recibos one payment issues, locatario and propietario, stating the same period and
/// received date and numbered consecutively with the locatario taking the lower number (spec Tests
/// 28 to 30). A pure function of the first number: where the number comes from is the counter, later.
/// </summary>
public sealed class ReciboPair
{
    public Recibo Locatario { get; }

    public Recibo Propietario { get; }

    /// <summary>Both recibos, locatario first.</summary>
    public IReadOnlyList<Recibo> Recibos { get; }

    private ReciboPair(Recibo locatario, Recibo propietario)
    {
        Locatario = locatario;
        Propietario = propietario;
        Recibos = Array.AsReadOnly(new[] { locatario, propietario });
    }

    /// <summary>
    /// Issues the pair. <paramref name="firstNumber"/> goes to the locatario recibo and the next
    /// number to the propietario recibo.
    /// </summary>
    public static ReciboPair Issue(
        long firstNumber,
        DateOnly period,
        DateOnly receivedOn,
        ReciboHeader header,
        ReciboDraftPair drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstNumber, 1L);

        // The propietario recibo takes firstNumber + 1, which must exist.
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(firstNumber, long.MaxValue);

        if (drafts.Locatario.Copy != ReciboCopy.Locatario || drafts.Propietario.Copy != ReciboCopy.Propietario)
        {
            throw new ArgumentException("Each draft must be the copy its slot names.", nameof(drafts));
        }

        return new ReciboPair(
            new Recibo(firstNumber, period, receivedOn, header, drafts.Locatario),
            new Recibo(firstNumber + 1, period, receivedOn, header, drafts.Propietario));
    }
}
