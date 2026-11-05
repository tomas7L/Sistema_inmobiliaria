using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>From the independent verification of slice S3, on the new pure functions.</summary>
public class ContractDraftAdversarialTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void FromAmounts_TiedShares_MatchTheEqualSplitsResidueRule()
    {
        var typed = RentSplit.FromAmounts(300m, [(C, 100m), (A, 100m), (B, 100m)])!;
        var equal = RentSplit.Equal([C, A, B]);
        Assert.Equal(equal.OrderBy(s => s.UnitId).Select(s => s.Percentage), typed.OrderBy(s => s.UnitId).Select(s => s.Percentage));
        Assert.Equal(33.333334m, typed.Single(s => s.UnitId == A).Percentage);
    }

    [Fact]
    public void FromAmounts_TheLargestAbsorbs_EvenWithAHigherId()
    {
        var typed = RentSplit.FromAmounts(300m, [(A, 100m), (B, 200m)])!;
        Assert.Equal(33.333333m, typed.Single(s => s.UnitId == A).Percentage);
        Assert.Equal(66.666667m, typed.Single(s => s.UnitId == B).Percentage);
    }

    [Fact]
    public void FromAmounts_AlwaysTotals100_At6Places_OverManyInputs()
    {
        var random = new Random(20261105);
        for (var i = 0; i < 20000; i++)
        {
            var count = random.Next(1, 7);
            var units = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
            var amounts = units.Select(u => (u, (decimal)random.Next(1, 5_000_000))).ToList();
            var canon = amounts.Sum(a => a.Item2);
            var shares = RentSplit.FromAmounts(canon, amounts)!;
            Assert.Equal(100m, shares.Sum(s => s.Percentage));
            Assert.All(shares, s => Assert.True(s.Percentage > 0m && s.Percentage == decimal.Round(s.Percentage, 6)));
        }
    }

    [Fact]
    public void FromAmounts_HugeAmounts_ReturnNull_NotThrow()
    {
        Assert.Null(RentSplit.FromAmounts(1000m, [(A, decimal.MaxValue), (B, decimal.MaxValue)]));
    }

    [Fact]
    public void Rules_HugeShares_AreRefused_NotThrown()
    {
        var draft = new ContractDraft
        {
            TenantId = A,
            LessorIds = [B],
            Shares = [new UnitShare(A, decimal.MaxValue), new UnitShare(B, decimal.MaxValue)],
            StartDate = new DateOnly(2026, 12, 1),
            NominalEndDate = new DateOnly(2028, 11, 30),
            MonthlyRent = 1000m,
        };

        Assert.Equal(ContractRefusal.SharesNotHundred, ContractDraftRules.Check(draft, new DateOnly(2026, 11, 5)));
    }

    [Theory]
    [InlineData(10, 2026, 11, 10, false)]
    [InlineData(10, 2026, 11, 11, true)]
    [InlineData(31, 2026, 11, 30, false)]
    public void FirstDueDateWarning_Boundary(int due, int y, int m, int d, bool warns)
    {
        var draft = new ContractDraft
        {
            StartDate = new DateOnly(2026, 11, 1),
            DueDay = due,
            HonorariosPercentage = 8m,
        };

        Assert.Equal(warns, ContractDraftRules.Warnings(draft, new DateOnly(y, m, d)).Any(w => w.Kind == ContractWarningKind.FirstDueDatePassed));
    }
}
