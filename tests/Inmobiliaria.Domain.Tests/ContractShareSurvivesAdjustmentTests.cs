using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// Spec test 17 / lease-contract "Confirmed adjustment leaves percentages untouched" — the
/// existing `SharePercentage` invariant, exercised for real for the first time by an actual
/// confirmed <see cref="RentAdjustment"/> rather than a generic <see cref="Contract.ChangeMonthlyRent"/> call.
/// </summary>
public class ContractShareSurvivesAdjustmentTests
{
    [Fact]
    public void ConfirmAdjustment_TwoUnitSixtyFortySplit_KeepsSharesSummingToOneHundred()
    {
        var contract = ContractTestFactory.CreateActive();
        var unitA = Guid.NewGuid();
        var unitB = Guid.NewGuid();
        contract.SetUnitShares([new UnitShare(unitA, 60m), new UnitShare(unitB, 40m)]);

        var indexId = Guid.NewGuid();
        var ipc = new RentAdjustmentIndexValue(
            indexId, indexId, new IndexPeriod(2026, 1), 8_000m, new IndexPeriod(2026, 6), 9_440m, 18m);
        var ripteId = Guid.NewGuid();
        var ripte = new RentAdjustmentIndexValue(
            ripteId, ripteId, new IndexPeriod(2026, 1), 1_200_000m, new IndexPeriod(2026, 6), 1_344_000m, 12m);

        var proposal = new AdjustmentProposal(
            450_000m, [ipc, ripte], CombinationRule.Average, coefficient: 15m, effectiveDate: new DateOnly(2026, 7, 1));

        contract.ConfirmAdjustment(Guid.NewGuid(), proposal, DateTimeOffset.UtcNow);

        Assert.Equal(517_500m, contract.MonthlyRent);
        Assert.Equal(60m, contract.Units.Single(u => u.UnitId == unitA).SharePercentage);
        Assert.Equal(40m, contract.Units.Single(u => u.UnitId == unitB).SharePercentage);
        Assert.Equal(100m, contract.Units.Sum(u => u.SharePercentage));
    }
}
