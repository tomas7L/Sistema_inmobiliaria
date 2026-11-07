using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-alquileres spec Tests 35, 36 and 56 on the pure rules: the window a clause reads, and the calendar after a cutover.</summary>
public class ClauseWindowTests
{
    private static readonly DateOnly DueJuly = new(2026, 7, 1);

    [Theory]
    [InlineData(0, 2026, 1, 2026, 7)]
    [InlineData(1, 2025, 12, 2026, 6)]
    [InlineData(2, 2025, 11, 2026, 5)]
    [InlineData(3, 2025, 10, 2026, 4)]
    public void TheWindow_EndsOffsetMonthsBeforeTheDueDate_AndKeepsTheIntervalsLength(
        int offset, int baseYear, int baseMonth, int endYear, int endMonth)
    {
        var window = AdjustmentWindow.For(DueJuly, intervalMonths: 6, offset);

        Assert.Equal(new IndexPeriod(baseYear, baseMonth), window.Base);
        Assert.Equal(new IndexPeriod(endYear, endMonth), window.End);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ConsecutiveAdjustments_ShareAMonth_SoNoneIsSkippedOrCountedTwice(int offset)
    {
        var start = new DateOnly(2026, 1, 1);
        var due = AdjustmentSchedule.NextDueDate(start, 6, lastEffectiveDate: null);

        for (var i = 0; i < 6; i++)
        {
            var next = AdjustmentSchedule.NextDueDate(start, 6, due);

            Assert.Equal(AdjustmentWindow.For(due, 6, offset).End, AdjustmentWindow.For(next, 6, offset).Base);
            due = next;
        }
    }

    [Fact]
    public void AClauseWithNoOffsetStated_ReadsTodaysWindow()
    {
        var clause = new AdjustmentClause(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], 6, RoundingRule.TruncateToWholePeso);

        Assert.Equal(1, clause.WindowOffsetMonths);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void AnOffsetOutOfRange_IsRefused_AndLeavesTheClauseAsItWas(int offset)
    {
        var clause = new AdjustmentClause(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], 6, RoundingRule.TruncateToWholePeso, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => clause.Correct(12, offset));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AdjustmentClause(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], 6, RoundingRule.TruncateToWholePeso, offset));
        Assert.Equal(6, clause.IntervalMonths);
        Assert.Equal(2, clause.WindowOffsetMonths);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(3, null)]
    [InlineData(4, ContractRefusal.ClauseInvalid)]
    [InlineData(-1, ContractRefusal.ClauseInvalid)]
    public void TheRules_TakeOffsetsFrom0To3(int offset, ContractRefusal? expected) =>
        Assert.Equal(expected, ContractDraftRules.CheckClause(new ClauseDraft([Guid.NewGuid()], 6, offset)));

    [Fact]
    public void AClauseDraftWithNoOffsetStated_ReadsTodaysWindow() =>
        Assert.Equal(1, new ClauseDraft([Guid.NewGuid()], 6).WindowOffsetMonths);

    [Theory]
    [InlineData(new[] { "ICL" }, 0)]
    [InlineData(new[] { "IPC" }, 2)]
    [InlineData(new[] { "RIPTE" }, 3)]
    [InlineData(new[] { "ICL", "RIPTE" }, 3)]
    [InlineData(new[] { "ICL", "IPC" }, 2)]
    [InlineData(new[] { "IPC", "Otro" }, 2)]
    [InlineData(new[] { "Otro" }, null)]
    [InlineData(new string[0], null)]
    public void TheSuggestion_IsWhatTheIndicesChosenNeed(string[] names, int? expected) =>
        Assert.Equal(expected, ClauseOffsetSuggestion.For(names));

    // ---- Test 56 --------------------------------------------------------------------------------------------------------

    [Fact]
    public void WithACutover_TheNextDueDate_IsTheFirstStepAfterIt() =>
        Assert.Equal(
            new DateOnly(2027, 3, 1),
            AdjustmentSchedule.NextDueDate(new DateOnly(2024, 9, 1), 6, lastEffectiveDate: null, new DateOnly(2026, 11, 5)));

    [Fact]
    public void ACutoverFallingOnAStep_TakesThatStepAsAlreadyInTheCanon() =>
        Assert.Equal(
            new DateOnly(2027, 3, 1),
            AdjustmentSchedule.NextDueDate(new DateOnly(2024, 9, 1), 6, lastEffectiveDate: null, new DateOnly(2026, 9, 1)));

    [Fact]
    public void ACutoverTheDayBeforeAStep_LeavesThatStepDue() =>
        Assert.Equal(
            new DateOnly(2026, 9, 1),
            AdjustmentSchedule.NextDueDate(new DateOnly(2024, 9, 1), 6, lastEffectiveDate: null, new DateOnly(2026, 8, 31)));

    [Fact]
    public void WithoutACutover_TodaysDerivationHolds() =>
        Assert.Equal(new DateOnly(2025, 3, 1), AdjustmentSchedule.NextDueDate(new DateOnly(2024, 9, 1), 6, lastEffectiveDate: null));

    [Fact]
    public void AConfirmedAdjustment_TakesOverFromTheCutover() =>
        Assert.Equal(
            new DateOnly(2027, 9, 1),
            AdjustmentSchedule.NextDueDate(new DateOnly(2024, 9, 1), 6, new DateOnly(2027, 3, 1), new DateOnly(2026, 11, 5)));

    [Fact]
    public void AStartThatIsNotOnThe1st_StillFallsOnThe1st() =>
        Assert.Equal(
            new DateOnly(2027, 3, 1),
            AdjustmentSchedule.NextDueDate(new DateOnly(2024, 9, 15), 6, lastEffectiveDate: null, new DateOnly(2026, 11, 5)));
}
