using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

public class RentSplitTests
{
    [Fact]
    public void Equal_AlwaysSumsToExactly100()
    {
        var unitIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var shares = RentSplit.Equal(unitIds);

        Assert.Equal(100m, shares.Sum(s => s.Percentage));
    }

    [Fact]
    public void Equal_ThreeWaySplit_ResidueGoesToLowestIdUnit()
    {
        var lowest = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var middle = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var highest = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var shares = RentSplit.Equal([highest, lowest, middle]);

        Assert.Equal(100m, shares.Sum(s => s.Percentage));
        Assert.Equal(33.333334m, shares.Single(s => s.UnitId == lowest).Percentage);
        Assert.Equal(33.333333m, shares.Single(s => s.UnitId == middle).Percentage);
        Assert.Equal(33.333333m, shares.Single(s => s.UnitId == highest).Percentage);
    }

    [Fact]
    public void SetUnitShares_SplitNotSummingTo100_IsRejected()
    {
        var contract = ContractTestFactory.CreateActive();
        var shares = new[] { new UnitShare(Guid.NewGuid(), 60m), new UnitShare(Guid.NewGuid(), 39.99m) };

        Assert.Throws<RentSplitInvariantException>(() => contract.SetUnitShares(shares));
    }

    [Fact]
    public void Constructor_WithNoUnits_IsRejected()
    {
        // design.md Decision 3 accepted that a row-level trigger can never fire for a contract
        // with no contract_units rows at all, so the database cannot catch this case. The domain
        // is the only place that can reject a lease which leases nothing, and it must actually
        // do so rather than merely be documented as doing so.
        Assert.Throws<RentSplitInvariantException>(() => new Contract(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 100_000m,
            unitShares: []));
    }

    [Fact]
    public void SetUnitShares_SameUnitTwice_IsRejected()
    {
        var contract = ContractTestFactory.CreateActive();
        var unit = Guid.NewGuid();

        // Sums to exactly 100%, so the sum rule alone would let this through. A contract
        // covering the same unit twice is still nonsense, and the caller should learn that
        // here rather than from a composite-key violation at save time.
        var shares = new[] { new UnitShare(unit, 50m), new UnitShare(unit, 50m) };

        Assert.Throws<RentSplitInvariantException>(() => contract.SetUnitShares(shares));
    }

    [Fact]
    public void ChangeMonthlyRent_LeavesStoredSharesUntouched()
    {
        var contract = ContractTestFactory.CreateActive();
        var unitA = Guid.NewGuid();
        var unitB = Guid.NewGuid();
        contract.SetUnitShares([new UnitShare(unitA, 50m), new UnitShare(unitB, 50m)]);

        contract.ChangeMonthlyRent(150_000m);

        Assert.Equal(100m, contract.Units.Sum(u => u.SharePercentage));
        Assert.All(contract.Units, u => Assert.Equal(50m, u.SharePercentage));
    }
}
