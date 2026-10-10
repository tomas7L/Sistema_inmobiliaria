using System.Reflection;
using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Tests;

public class RecargoTests
{
    /// <summary>The agency's lease terms: 2% per day, counted from the due day itself.</summary>
    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    /// <summary>July 2026 rent, falling due on the 10th.</summary>
    private static readonly DateOnly DueJuly10 = new(2026, 7, 10);

    /// <summary>
    /// Spec test 16: the figure from the agency's own receipts. Rent of 500,000 paid on the 15th,
    /// due on the 10th, is five days of mora: 500,000 × 2% × 5 = 50,000.
    /// </summary>
    [Fact]
    public void Recargo_MatchesTheAgencysOwnReceipt()
    {
        var recargo = RecargoMath.For(500_000m, DueJuly10, new DateOnly(2026, 7, 15), LeaseTerms);

        Assert.Equal(50_000m, recargo);
    }

    /// <summary>
    /// Spec test 16: the same inputs asked on three later and later dates give a growing figure,
    /// and the ledger gains nothing. This is the load-bearing property of the whole design —
    /// recargo is derived on demand, so three months of mora is three months of silence in the
    /// account rather than ninety rows.
    ///
    /// The account is passed nowhere and cannot be touched today. The assertion is here so that if
    /// somebody later gives this function an account to write to, this test is what stops them.
    /// </summary>
    [Fact]
    public void AskingRepeatedly_GrowsTheFigure_AndCreatesNothing()
    {
        var account = new ContractAccount(Guid.NewGuid(), Guid.NewGuid());
        account.Append(AccountMovement.RentAccrual(
            Guid.NewGuid(), 500_000m, new DateOnly(2026, 7, 1), DueJuly10));

        var afterOneWeek = RecargoMath.For(500_000m, DueJuly10, DueJuly10.AddDays(7), LeaseTerms);
        var afterTwoWeeks = RecargoMath.For(500_000m, DueJuly10, DueJuly10.AddDays(14), LeaseTerms);
        var afterThreeWeeks = RecargoMath.For(500_000m, DueJuly10, DueJuly10.AddDays(21), LeaseTerms);

        Assert.True(afterOneWeek < afterTwoWeeks);
        Assert.True(afterTwoWeeks < afterThreeWeeks);

        // Three questions asked, one movement in the account: the rent that was already there.
        Assert.Single(account.Movements);

        // And asking the same question twice gives the same answer, because nothing accumulated.
        Assert.Equal(
            afterOneWeek,
            RecargoMath.For(500_000m, DueJuly10, DueJuly10.AddDays(7), LeaseTerms));
    }

    /// <summary>
    /// Spec test 17: paying on the due day owes nothing. The receipts count day-of-payment minus
    /// due-day, so the due day itself is zero days of mora rather than one.
    /// </summary>
    [Fact]
    public void OnTheDueDay_NothingIsOwed()
    {
        Assert.Equal(0m, RecargoMath.For(500_000m, DueJuly10, DueJuly10, LeaseTerms));
    }

    [Fact]
    public void BeforeTheDueDay_NothingIsOwed()
    {
        var early = RecargoMath.For(500_000m, DueJuly10, new DateOnly(2026, 7, 3), LeaseTerms);

        Assert.Equal(0m, early);
    }

    [Fact]
    public void TheDayAfterTheDueDay_IsExactlyOneDay()
    {
        var oneDay = RecargoMath.For(500_000m, DueJuly10, new DateOnly(2026, 7, 11), LeaseTerms);

        Assert.Equal(10_000m, oneDay);
    }

    /// <summary>Spec test 18: nothing owed means nothing accrues, whatever the dates say.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-25_000)]
    public void WithNothingOwed_NothingAccrues(decimal amountOwed)
    {
        var recargo = RecargoMath.For(
            amountOwed, DueJuly10, new DateOnly(2026, 12, 31), LeaseTerms);

        Assert.Equal(0m, recargo);
    }

    /// <summary>
    /// Spec test 19: simple interest, never compound. Two days is exactly twice one day — if it
    /// compounded, the second day would charge on 510,000 rather than on 500,000.
    /// </summary>
    [Fact]
    public void Recargo_IsSimpleAndNeverCompounds()
    {
        var oneDay = RecargoMath.For(500_000m, DueJuly10, new DateOnly(2026, 7, 11), LeaseTerms);
        var twoDays = RecargoMath.For(500_000m, DueJuly10, new DateOnly(2026, 7, 12), LeaseTerms);

        Assert.Equal(oneDay * 2, twoDays);
    }

    /// <summary>
    /// Spec test 26: whole pesos, truncated toward zero. 398,840 over six days computes to exactly
    /// 47,860.80, and the tenant is charged 47,860 — the project truncates rather than rounding,
    /// the same way the adjusted canon does. The centavos are dropped, never rounded up.
    /// </summary>
    [Fact]
    public void Recargo_IsTruncatedToWholePesos_NeverRounded()
    {
        var asOf = DueJuly10.AddDays(6);

        // The untruncated product, stated here so the test fails loudly if the arithmetic changes
        // rather than silently agreeing with a new wrong answer.
        Assert.Equal(47_860.80m, 398_840m * 0.02m * 6);

        var recargo = RecargoMath.For(398_840m, DueJuly10, asOf, LeaseTerms);

        Assert.Equal(47_860m, recargo);
    }

    /// <summary>
    /// Spec test 17: there is no ceiling. Two hundred days of mora on 500,000 is exactly 2,000,000
    /// — four times the debt — and the function returns it. What limits an unpaid debt is legal
    /// rather than arithmetic, and a cap here would compute something the two parties never signed.
    /// The exact figure is asserted, not merely that it is large.
    /// </summary>
    [Fact]
    public void TwoHundredDaysOverdue_AccruesTheFullAmount_WithNoCeiling()
    {
        var recargo = RecargoMath.For(
            500_000m, DueJuly10, DueJuly10.AddDays(200), LeaseTerms);

        Assert.Equal(2_000_000m, recargo);
        Assert.True(recargo > 500_000m * 2);
    }

    /// <summary>
    /// Spec test 22: a novación freezes the surcharge on the day it was signed. Asking any later
    /// date yields the same figure, because the obligation was extinguished and replaced — nothing
    /// remained to accrue against.
    /// </summary>
    [Fact]
    public void ANovacion_FreezesTheRecargoOnTheDayItWasSigned()
    {
        var signedOn = new DateOnly(2026, 11, 15);
        var daysToFreeze = signedOn.DayNumber - DueJuly10.DayNumber;

        var atSigning = RecargoMath.For(500_000m, DueJuly10, signedOn, LeaseTerms, frozenOn: signedOn);
        var fiveWeeksLater = RecargoMath.For(
            500_000m, DueJuly10, new DateOnly(2026, 12, 20), LeaseTerms, frozenOn: signedOn);

        Assert.Equal(500_000m * 0.02m * daysToFreeze, atSigning);
        Assert.Equal(atSigning, fiveWeeksLater);
    }

    /// <summary>
    /// The freeze is a date, not a flag, so a question asked about an earlier day still reads
    /// correctly. A boolean would have rewritten history: every past balance would have reported
    /// whatever the freeze later turned out to be.
    /// </summary>
    [Fact]
    public void BeforeTheFreeze_TheRecargoStillReadsAsItWasThen()
    {
        var signedOn = new DateOnly(2026, 11, 15);

        var midway = RecargoMath.For(
            500_000m, DueJuly10, new DateOnly(2026, 7, 15), LeaseTerms, frozenOn: signedOn);

        Assert.Equal(50_000m, midway);
    }

    /// <summary>
    /// Spec test 23: a defaulted payment plan accrues on the balance still owing, not on the
    /// original debt. Two instalments of 100,000 were paid against 500,000, so the surcharge runs
    /// on the remaining 300,000 — the owner's own words on how it should work.
    /// </summary>
    [Fact]
    public void ADefaultedPlan_AccruesOnTheRemainingBalance()
    {
        var planTerms = new RecargoTerms(0.02m, graceDays: 0);
        var defaultedOn = new DateOnly(2026, 9, 10);

        var onRemaining = RecargoMath.For(
            300_000m, defaultedOn, new DateOnly(2026, 9, 20), planTerms);

        Assert.Equal(60_000m, onRemaining);

        // And it is strictly less than it would have been on the untouched debt, which is the whole
        // point of resuming payments.
        var onOriginal = RecargoMath.For(
            500_000m, defaultedOn, new DateOnly(2026, 9, 20), planTerms);
        Assert.True(onRemaining < onOriginal);
    }

    /// <summary>
    /// Spec test 24: a plan may grant grace days, and nothing accrues inside them. The agency's
    /// leases carry none; a plan carries whatever its two parties signed.
    /// </summary>
    [Fact]
    public void GraceDays_DelayTheStartWithoutChangingTheRate()
    {
        var withGrace = new RecargoTerms(0.02m, graceDays: 5);

        var insideGrace = RecargoMath.For(
            500_000m, DueJuly10, new DateOnly(2026, 7, 15), withGrace);
        var oneDayPastGrace = RecargoMath.For(
            500_000m, DueJuly10, new DateOnly(2026, 7, 16), withGrace);

        Assert.Equal(0m, insideGrace);
        Assert.Equal(10_000m, oneDayPastGrace);
    }

    /// <summary>
    /// Spec test 25: the rate is an argument, never a constant. A plan signed at 1% charges half
    /// what a lease at 2% charges over the same days.
    /// </summary>
    [Fact]
    public void TheRate_ComesFromTheTerms_NotFromTheCode()
    {
        var asOf = new DateOnly(2026, 7, 20);

        var atTwoPercent = RecargoMath.For(500_000m, DueJuly10, asOf, LeaseTerms);
        var atOnePercent = RecargoMath.For(
            500_000m, DueJuly10, asOf, new RecargoTerms(0.01m, graceDays: 0));

        Assert.Equal(100_000m, atTwoPercent);
        Assert.Equal(50_000m, atOnePercent);
    }

    /// <summary>
    /// Spec test 19, the standing guard: no rate is baked into the calculator. Asserted over the
    /// source text, because a literal written inside the method body — <c>amountOwed * 0.02m *
    /// days</c> — is invisible to reflection. Reflection sees declared members, so it catches a
    /// <c>const</c> and misses the thing most likely to be typed in a hurry.
    ///
    /// The file is located by walking up to the solution, the same way the infrastructure guards
    /// do, and the test fails loudly if it is not found rather than passing on an empty string.
    /// </summary>
    [Fact]
    public void NoRateConstant_AppearsInRecargoMath()
    {
        var source = ReadDomainSource("Accounts/RecargoMath.cs");

        Assert.NotEmpty(source);

        // Executable lines only: the doc comments legitimately mention 2% when explaining that the
        // rate is NOT held here, and a guard that forbade the word would forbid the explanation.
        var code = source
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

        foreach (var line in code)
        {
            Assert.DoesNotContain("0.02", line);
            Assert.DoesNotContain("0.01", line);
        }

        // And no declared member holds one either, which is what reflection is actually good for.
        Assert.Empty(typeof(RecargoMath).GetFields(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
    }

    private static string ReadDomainSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Inmobiliaria.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (Inmobiliaria.sln not found).");
        }

        var file = Path.Combine(
            directory.FullName, "src", "Inmobiliaria.Domain", relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(file))
        {
            throw new FileNotFoundException(
                $"The guard cannot read its subject, so it proves nothing: {file}", file);
        }

        return File.ReadAllText(file);
    }

    [Fact]
    public void Terms_AreRequired()
    {
        Assert.Throws<ArgumentNullException>(
            () => RecargoMath.For(500_000m, DueJuly10, new DateOnly(2026, 7, 20), null!));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.5)]
    public void AnImpossibleRate_IsRefused(decimal dailyRate)
    {
        Assert.Throws<ArgumentException>(() => new RecargoTerms(dailyRate, graceDays: 0));
    }

    [Fact]
    public void NegativeGraceDays_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => new RecargoTerms(0.02m, graceDays: -1));
    }

    /// <summary>
    /// A rate of zero is legitimate, not an error: it is how a plan that forgives the surcharge
    /// entirely is recorded, and the record must still say somebody chose that.
    /// </summary>
    [Fact]
    public void ARateOfZero_IsValidAndAccruesNothing()
    {
        var forgiven = new RecargoTerms(0m, graceDays: 0);

        var recargo = RecargoMath.For(500_000m, DueJuly10, new DateOnly(2026, 8, 31), forgiven);

        Assert.Equal(0m, recargo);
    }
}
