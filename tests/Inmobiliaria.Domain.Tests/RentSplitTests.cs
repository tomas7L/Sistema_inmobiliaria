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
