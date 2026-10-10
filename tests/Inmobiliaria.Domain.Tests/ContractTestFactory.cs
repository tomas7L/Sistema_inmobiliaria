using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

/// <summary>Shared builders for domain unit tests — no infrastructure involved.</summary>
internal static class ContractTestFactory
{
    /// <summary>
    /// A valid Active contract covering one unit at 100%. A contract cannot be constructed
    /// without at least one unit, so every builder here supplies a default split.
    ///
    /// Every parameter is optional and keeps its previous value, so existing callers are
    /// unaffected: the accrual schedule needs contracts with specific dates, canons and due days,
    /// and inventing a second factory beside this one would leave two builders to keep in step.
    /// </summary>
    public static Contract CreateActive(
        decimal? honorariosPercentage = null,
        DateOnly? startDate = null,
        DateOnly? nominalEndDate = null,
        decimal monthlyRent = 100_000m,
        int dueDay = Contract.DefaultDueDay) =>
        new(
            Guid.NewGuid(),
            startDate ?? new DateOnly(2026, 1, 1),
            nominalEndDate ?? new DateOnly(2027, 12, 31),
            monthlyRent: monthlyRent,
            unitShares: [new UnitShare(Guid.NewGuid(), 100m)],
            honorariosPercentage: honorariosPercentage,
            dueDay: dueDay);

    /// <summary>
    /// Confirms one adjustment on <paramref name="contract"/>, raising the canon by
    /// <paramref name="percentage"/> from <paramref name="previousCanon"/> and taking effect on
    /// <paramref name="effectiveDate"/>. The index values are the minimum a proposal needs; the
    /// accrual tests care only about which canon was in force when, not how it was derived.
    /// </summary>
    public static RentAdjustment ConfirmAdjustment(
        Contract contract,
        decimal previousCanon,
        decimal percentage,
        DateOnly effectiveDate)
    {
        var indexId = Guid.NewGuid();
        var indexValue = new RentAdjustmentIndexValue(
            indexId, indexId, new IndexPeriod(2026, 1), 100m, new IndexPeriod(2026, 6), 110m, percentage);

        var proposal = new AdjustmentProposal(
            previousCanon, [indexValue], CombinationRule.Single, percentage, effectiveDate);

        return contract.ConfirmAdjustment(
            Guid.NewGuid(), proposal, DateTimeOffset.UtcNow, Guid.NewGuid());
    }

    public static Party CreateParty(string firstName, string lastName) =>
        new(Guid.NewGuid(), firstName, lastName);
}
