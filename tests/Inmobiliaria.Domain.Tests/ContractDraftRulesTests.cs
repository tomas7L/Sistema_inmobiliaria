using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-alquileres spec Tests 12, 15 to 17, 19, 21 and 22 on the form's pure rules, with today fixed.</summary>
public class ContractDraftRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly Guid UnitA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid UnitB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static ContractDraft Valid() => new()
    {
        TenantId = Guid.NewGuid(),
        LessorIds = [Guid.NewGuid()],
        Shares = [new UnitShare(UnitA, 100m)],
        StartDate = new DateOnly(2026, 12, 1),
        NominalEndDate = new DateOnly(2028, 11, 30),
        MonthlyRent = 450_000m,
        HonorariosPercentage = 8m,
    };

    private static ContractRefusal? Check(ContractDraft draft) => ContractDraftRules.Check(draft, Today);

    [Fact]
    public void ACompleteDraft_IsAccepted_WithTheDueDayDefaultingTo10()
    {
        Assert.Null(Check(Valid()));
        Assert.Equal(10, Valid().DueDay);
    }

    /// <summary>Spec Test 12: each missing part names itself.</summary>
    [Fact]
    public void NoTenantLessorOrUnit_IsEachRefused()
    {
        Assert.Equal(ContractRefusal.TenantMissing, Check(Valid() with { TenantId = null }));
        Assert.Equal(ContractRefusal.LessorMissing, Check(Valid() with { LessorIds = [] }));
        Assert.Equal(ContractRefusal.UnitMissing, Check(Valid() with { Shares = [] }));
        Assert.Equal(ContractRefusal.DatesMissing, Check(Valid() with { StartDate = null }));
        Assert.Equal(ContractRefusal.DatesMissing, Check(Valid() with { NominalEndDate = null }));
    }

    /// <summary>Spec Test 15: a start not on the 1st is refused here, while the domain keeps tolerating one for existing rules (F9).</summary>
    [Fact]
    public void AStartNotOnTheFirst_IsRefusedByTheRules_AndToleratedByTheContract()
    {
        var mid = new DateOnly(2026, 12, 15);

        Assert.Equal(ContractRefusal.StartNotOnTheFirst, Check(Valid() with { StartDate = mid }));
        Assert.Equal(mid, new Contract(Guid.NewGuid(), mid, new DateOnly(2028, 11, 30), 100m, [new UnitShare(UnitA, 100m)]).StartDate);
        Assert.Contains("día 1", ContractRefusal.StartNotOnTheFirst.OperatorText());
    }

    [Fact]
    public void AnEndBeforeTheStart_IsRefused()
    {
        Assert.Equal(ContractRefusal.EndBeforeStart, Check(Valid() with { NominalEndDate = new DateOnly(2026, 11, 30) }));
    }

    /// <summary>Spec Test 16: a start in the current month is accepted, and warns only once the first due date has passed.</summary>
    [Theory]
    [InlineData(5, false)]
    [InlineData(10, false)]
    [InlineData(11, true)]
    [InlineData(15, true)]
    public void AStartInTheCurrentMonth_WarnsOnlyOnceItsFirstDueDateHasPassed(int day, bool warns)
    {
        var today = new DateOnly(2026, 11, day);
        var draft = Valid() with { StartDate = new DateOnly(2026, 11, 1) };

        Assert.Null(ContractDraftRules.Check(draft, today));
        Assert.Equal(warns, ContractDraftRules.Warnings(draft, today).Any(w => w.Kind == ContractWarningKind.FirstDueDatePassed));
    }

    /// <summary>Spec R7: the warning says the month is overdue, in words.</summary>
    [Fact]
    public void TheOverdueWarning_NamesTheMonth()
    {
        var warning = Assert.Single(ContractDraftRules.Warnings(Valid() with { StartDate = new DateOnly(2026, 11, 1) }, Today));

        Assert.Contains("noviembre", warning.OperatorText());
        Assert.Contains("vencido", warning.OperatorText());
    }

    /// <summary>A due day longer than the month clamps as the schedule clamps it.</summary>
    [Fact]
    public void TheOverdueWarning_UsesTheClampedDueDate()
    {
        var draft = Valid() with { StartDate = new DateOnly(2027, 2, 1), DueDay = 31 };

        Assert.Empty(ContractDraftRules.Warnings(draft, new DateOnly(2027, 2, 28)));

        var thirtieth = Valid() with { StartDate = new DateOnly(2027, 4, 1), DueDay = 31 };

        Assert.Empty(ContractDraftRules.Warnings(thirtieth, new DateOnly(2027, 4, 30)));
    }

    /// <summary>A cutover date carries those months, so nothing is overdue because of the start.</summary>
    [Fact]
    public void ACutover_DoesNotWarnAboutTheFirstDueDate()
    {
        var draft = Valid() with { StartDate = new DateOnly(2026, 11, 1), CutoverDate = new DateOnly(2026, 11, 12) };

        Assert.Empty(ContractDraftRules.Warnings(draft, Today));
    }

    /// <summary>Spec Test 22: before the current month with no cutover is stopped, saying an Admin has to load it.</summary>
    [Fact]
    public void AStartBeforeTheCurrentMonth_WithNoCutover_SaysAnAdminHasToLoadIt()
    {
        var draft = Valid() with { StartDate = new DateOnly(2026, 10, 1) };

        Assert.Equal(ContractRefusal.StartBeforeCurrentMonth, Check(draft));
        Assert.Contains("Admin", ContractRefusal.StartBeforeCurrentMonth.OperatorText());
    }

    /// <summary>Spec Test 21: the cutover's bounds, as the domain has them.</summary>
    [Fact]
    public void TheCutoversBounds_AreRefusedByTheRules()
    {
        var old = Valid() with { StartDate = new DateOnly(2024, 11, 1) };

        Assert.Null(Check(old with { CutoverDate = Today }));
        Assert.Null(Check(old with { CutoverDate = new DateOnly(2024, 11, 1) }));
        Assert.Equal(ContractRefusal.CutoverAfterToday, Check(old with { CutoverDate = Today.AddDays(1) }));
        Assert.Equal(ContractRefusal.CutoverBeforeStart, Check(old with { CutoverDate = new DateOnly(2024, 10, 31) }));
    }

    /// <summary>Spec Test 21: one rule, so the form and the domain cannot disagree about the bounds.</summary>
    [Theory]
    [InlineData(2026, 11, 16, CutoverRefusal.AfterToday)]
    [InlineData(2024, 10, 31, CutoverRefusal.BeforeContractStart)]
    [InlineData(2026, 11, 15, null)]
    public void CheckCutoverBounds_IsTheDomainsRule(int year, int month, int day, CutoverRefusal? expected)
    {
        Assert.Equal(expected, Contract.CheckCutoverBounds(new DateOnly(year, month, day), new DateOnly(2024, 11, 1), Today));
    }

    /// <summary>Spec Test 17: a canon that is not a positive whole peso, or does not fit its column.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100.50")]
    [InlineData("1000000000000")]
    public void ACanonThatIsNotAPositiveWholePeso_IsRefused(string? rent)
    {
        var draft = Valid() with { MonthlyRent = rent is null ? null : decimal.Parse(rent, System.Globalization.CultureInfo.InvariantCulture) };

        Assert.Equal(ContractRefusal.RentNotWholePositive, Check(draft));
    }

    /// <summary>Spec Test 17: shares not totalling 100%, or that cannot be stored exactly.</summary>
    [Fact]
    public void SharesThatDoNotTotal100_AreRefused()
    {
        ContractRefusal? Shares(params UnitShare[] shares) => Check(Valid() with { Shares = shares });

        Assert.Equal(ContractRefusal.SharesNotHundred, Shares(new UnitShare(UnitA, 60m), new UnitShare(UnitB, 39m)));
        Assert.Equal(ContractRefusal.SharesNotHundred, Shares(new UnitShare(UnitA, 100m), new UnitShare(UnitB, 0m)));
        Assert.Equal(ContractRefusal.SharesNotHundred, Shares(new UnitShare(UnitA, 50m), new UnitShare(UnitA, 50m)));
        Assert.Equal(ContractRefusal.SharesNotHundred, Shares(new UnitShare(UnitA, 33.3333333m), new UnitShare(UnitB, 66.6666667m)));
        Assert.Null(Shares(new UnitShare(UnitA, 60m), new UnitShare(UnitB, 40m)));
    }

    /// <summary>Spec Test 19: empty warns and proceeds, 0% is an entered rate, and neither is refused.</summary>
    [Fact]
    public void EmptyAndZeroRates_StayDifferent()
    {
        var empty = Valid() with { HonorariosPercentage = null };
        var zero = Valid() with { HonorariosPercentage = 0m };

        Assert.Null(Check(empty));
        Assert.Null(Check(zero));
        Assert.Equal(ContractWarningKind.NoHonorarios, Assert.Single(ContractDraftRules.Warnings(empty, Today)).Kind);
        Assert.Empty(ContractDraftRules.Warnings(zero, Today));
    }

    [Theory]
    [InlineData("100.01")]
    [InlineData("-0.01")]
    [InlineData("8.555")]
    public void ARateOutOfRange_IsRefused(string rate)
    {
        var draft = Valid() with { HonorariosPercentage = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture) };

        Assert.Equal(ContractRefusal.HonorariosOutOfRange, Check(draft));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void ADueDayOutOfRange_IsRefused(int dueDay)
    {
        Assert.Equal(ContractRefusal.DueDayOutOfRange, Check(Valid() with { DueDay = dueDay }));
    }

    [Fact]
    public void AFeePlan_NeedsItsAmountAndInstalments()
    {
        Assert.Null(Check(Valid() with { FeePlanAmount = 120_000m, FeePlanInstalments = 6 }));
        Assert.Equal(ContractRefusal.FeePlanInvalid, Check(Valid() with { FeePlanAmount = 120_000m }));
        Assert.Equal(ContractRefusal.FeePlanInvalid, Check(Valid() with { FeePlanInstalments = 3 }));
        Assert.Equal(ContractRefusal.FeePlanInvalid, Check(Valid() with { FeePlanAmount = 120_000m, FeePlanInstalments = 7 }));
        Assert.Equal(ContractRefusal.FeePlanInvalid, Check(Valid() with { FeePlanAmount = 0m, FeePlanInstalments = 1 }));
    }

    [Fact]
    public void AClause_NeedsAnIndexWithoutRepeats_AndAnInterval()
    {
        var index = Guid.NewGuid();

        Assert.Null(Check(Valid() with { Clause = new ClauseDraft([index], 6) }));
        Assert.Equal(ContractRefusal.ClauseInvalid, Check(Valid() with { Clause = new ClauseDraft([], 6) }));
        Assert.Equal(ContractRefusal.ClauseInvalid, Check(Valid() with { Clause = new ClauseDraft([index, index], 6) }));
        Assert.Equal(ContractRefusal.ClauseInvalid, Check(Valid() with { Clause = new ClauseDraft([index], 0) }));
        Assert.Equal(ContractRefusal.ClauseInvalid, Check(Valid() with { Clause = new ClauseDraft([index], 61) }));
    }

    /// <summary>Spec Test 17: amounts convert to percentages; the residue goes to the largest share, ties to the lowest id.</summary>
    [Fact]
    public void TypedAmounts_ConvertWithTheResidueRule()
    {
        var shares = RentSplit.FromAmounts(300_000m, [(UnitB, 100_000m), (UnitA, 100_000m), (Guid.Parse("00000000-0000-0000-0000-00000000000c"), 100_000m)])!;

        Assert.Equal(100m, shares.Sum(s => s.Percentage));
        Assert.Equal(33.333334m, shares.Single(s => s.UnitId == UnitA).Percentage);
        Assert.Equal(33.333333m, shares.Single(s => s.UnitId == UnitB).Percentage);

        var unequal = RentSplit.FromAmounts(300_000m, [(UnitA, 100_000m), (UnitB, 200_000m)])!;

        Assert.Equal(100m, unequal.Sum(s => s.Percentage));
        Assert.Equal(33.333333m, unequal.Single(s => s.UnitId == UnitA).Percentage);
        Assert.Equal(66.666667m, unequal.Single(s => s.UnitId == UnitB).Percentage);
    }

    [Fact]
    public void TypedAmounts_ThatDoNotTotalTheCanon_GiveNoSplit()
    {
        Assert.Null(RentSplit.FromAmounts(300_000m, [(UnitA, 100_000m), (UnitB, 100_000m)]));
        Assert.Null(RentSplit.FromAmounts(300_000m, [(UnitA, 300_000m), (UnitB, 0m)]));
    }

    /// <summary>Spec R7: the word "garante" appears nowhere an operator reads.</summary>
    [Fact]
    public void NoTextSaysGarante()
    {
        var texts = Enum.GetValues<ContractRefusal>().Select(r => r.OperatorText("x"))
            .Concat(Enum.GetValues<ContractWarningKind>().Select(k => new ContractWarning(k, Today, "x").OperatorText()));

        Assert.DoesNotContain(texts, text => text.Contains("garante", StringComparison.OrdinalIgnoreCase));
    }
}
