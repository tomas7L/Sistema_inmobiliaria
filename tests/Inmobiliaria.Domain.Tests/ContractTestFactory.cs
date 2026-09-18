using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

/// <summary>Shared builders for domain unit tests — no infrastructure involved.</summary>
internal static class ContractTestFactory
{
    /// <summary>
    /// A valid Active contract covering one unit at 100%. A contract cannot be constructed
    /// without at least one unit, so every builder here supplies a default split.
    /// </summary>
    public static Contract CreateActive(decimal? honorariosPercentage = null) =>
        new(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 100_000m,
            unitShares: [new UnitShare(Guid.NewGuid(), 100m)],
            honorariosPercentage: honorariosPercentage);

    public static Party CreateParty(string firstName, string lastName) =>
        new(Guid.NewGuid(), firstName, lastName);
}
