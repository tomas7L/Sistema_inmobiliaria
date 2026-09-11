using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

/// <summary>Shared builders for domain unit tests — no infrastructure involved.</summary>
internal static class ContractTestFactory
{
    public static Contract CreateActive() =>
        new(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 100_000m);

    public static Party CreateParty(string firstName, string lastName) =>
        new(Guid.NewGuid(), firstName, lastName);
}
