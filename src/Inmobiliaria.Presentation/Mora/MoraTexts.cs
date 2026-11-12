namespace Inmobiliaria.Presentation.Mora;

/// <summary>
/// What the Mora screen says. The final wording waits for Mateo's screens, as every S12 text does (mora design §4.1).
/// </summary>
public static class MoraTexts
{
    /// <summary>What the figures cover, and what they never do (contract-account "The Balance Names What It Covers").</summary>
    public const string Coverage = Domain.Accounts.BalanceCoverage.Sentence;

    public const string Empty = "No hay ninguna cuenta en mora.";

    public const string AccountGone = "Esta cuenta ya no se puede abrir en la caja. Actualizá la lista.";

    public static string FrozenOn(string date) => $"Recargo congelado el {date}";
}
