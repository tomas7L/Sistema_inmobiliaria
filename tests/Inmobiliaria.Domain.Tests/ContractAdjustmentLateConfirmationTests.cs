using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// Covers spec tests 11 and 12 only PARTIALLY — no billing exists in this change, so there is
/// nothing to assert "no retroactive charge was generated" against. Only what is assertable
/// today: confirming late appends exactly one adjustment and no other row, and the canon
/// afterward equals the new canon. The current-account change must re-assert both tests for
/// real once billing exists (known coverage gap, carried forward in tasks.md).
/// </summary>
public class ContractAdjustmentLateConfirmationTests
{
    [Fact]
    public void ConfirmAdjustment_ConfirmedLate_AppendsExactlyOneAdjustment()
    {
        var contract = ContractTestFactory.CreateActive();
        var indexId = Guid.NewGuid();
        var indexValue = new RentAdjustmentIndexValue(
            indexId, indexId, new IndexPeriod(2026, 1), 100m, new IndexPeriod(2026, 6), 110m, 10m);

        // Due 2026-07-01, confirmed 2026-10-01: three whole months late.
        var proposal = new AdjustmentProposal(
            100_000m, [indexValue], CombinationRule.Single, coefficient: 10m,
            effectiveDate: new DateOnly(2026, 11, 1), monthsLate: 3);

        contract.ConfirmAdjustment(Guid.NewGuid(), proposal, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Single(contract.Adjustments);
    }

    [Fact]
    public void ConfirmAdjustment_ConfirmedLate_CanonAfterwardEqualsTheNewCanon()
    {
        var contract = ContractTestFactory.CreateActive();
        var indexId = Guid.NewGuid();
        var indexValue = new RentAdjustmentIndexValue(
            indexId, indexId, new IndexPeriod(2026, 1), 100m, new IndexPeriod(2026, 6), 110m, 10m);

        var proposal = new AdjustmentProposal(
            100_000m, [indexValue], CombinationRule.Single, coefficient: 10m,
            effectiveDate: new DateOnly(2026, 11, 1), monthsLate: 3);

        var adjustment = contract.ConfirmAdjustment(Guid.NewGuid(), proposal, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Equal(adjustment.NewCanon, contract.MonthlyRent);
        Assert.Equal(110_000m, contract.MonthlyRent);
    }
}
