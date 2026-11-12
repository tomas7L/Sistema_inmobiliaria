namespace Inmobiliaria.Domain.Accounts;

/// <summary>
/// What an account's figures cover, and what they never do (contract-account "The Balance Names What It Covers"). One
/// sentence, so the Mora screen and a tenant's notice say it in the same words (notificaciones spec Requirement 2).
/// </summary>
public static class BalanceCoverage
{
    public const string Sentence =
        "Los importes incluyen alquiler, cuotas de honorarios y recargo. No incluyen tasa municipal, seguros ni otros conceptos. "
        + "El total adeudado ya incluye el recargo.";
}
