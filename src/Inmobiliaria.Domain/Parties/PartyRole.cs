namespace Inmobiliaria.Domain.Parties;

/// <summary>
/// A role a <see cref="Party"/> can hold on a <see cref="Domain.Leasing.Contract"/>.
/// Lessor (locador), Tenant (locatario), Codebtor (codeudor — solidary co-debtor).
/// There is intentionally no "Garante" (guarantor) value: it is a different liability
/// regime under Argentine law and must never appear here.
/// </summary>
public enum PartyRole
{
    Lessor,
    Tenant,
    Codebtor
}
