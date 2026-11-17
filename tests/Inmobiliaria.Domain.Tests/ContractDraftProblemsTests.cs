using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// gestion-de-alquileres spec Requirements 27 and 29, Decisions 44, 50 and 51, Tests 106 and 113 to 116: every problem of a
/// contract's form at once, each with its field, and the rules that make its parties and dates hold together.
/// </summary>
public class ContractDraftProblemsTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly Guid UnitA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Juan = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    private static ContractDraft Valid() => new()
    {
        TenantId = Juan,
        LessorIds = [Guid.NewGuid()],
        Shares = [new UnitShare(UnitA, 100m)],
        StartDate = new DateOnly(2026, 12, 1),
        NominalEndDate = new DateOnly(2028, 11, 30),
        MonthlyRent = 450_000m,
        HonorariosPercentage = 8m,
    };

    private static IReadOnlyList<ContractProblem> Problems(ContractDraft draft) => ContractDraftRules.Problems(draft, Today);

    [Fact]
    public void AValidDraft_HasNoProblems()
    {
        Assert.Empty(Problems(Valid()));
    }

    /// <summary>Spec Test 106 and its scenario: no tenant, an end before the start and an unreadable rent give three, together.</summary>
    [Fact]
    public void ThreeMistakes_GiveThreeProblems_EachWithItsField()
    {
        var draft = Valid() with { TenantId = null, NominalEndDate = new DateOnly(2026, 6, 30), MonthlyRent = null };

        Assert.Equal(
            [
                new ContractProblem(ContractField.Tenant, ContractRefusal.TenantMissing),
                new ContractProblem(ContractField.EndDate, ContractRefusal.EndBeforeStart),
                new ContractProblem(ContractField.Rent, ContractRefusal.RentNotWholePositive),
            ],
            Problems(draft));
    }

    /// <summary>The port's guard stays the first problem, in the form's order.</summary>
    [Fact]
    public void Check_IsTheFirstProblem()
    {
        var draft = Valid() with { LessorIds = [], MonthlyRent = 0m };

        Assert.Equal(ContractRefusal.LessorMissing, ContractDraftRules.Check(draft, Today));
        Assert.Equal(2, Problems(draft).Count);
    }

    /// <summary>Each missing date is said beside its own field.</summary>
    [Fact]
    public void EachMissingDate_IsBesideItsField()
    {
        Assert.Equal(
            [
                new ContractProblem(ContractField.StartDate, ContractRefusal.DatesMissing),
                new ContractProblem(ContractField.EndDate, ContractRefusal.DatesMissing),
            ],
            Problems(Valid() with { StartDate = null, NominalEndDate = null }));
    }

    /// <summary>
    /// Spec Test 113: the tenant cannot be a codeudor of the same contract, said under that codeudor's row (Mateo's canvas,
    /// Tomás 2026-11-16); a lessor who is also the tenant can.
    /// </summary>
    [Fact]
    public void TheTenantAsCodebtor_IsRefusedOnItsRow_ButTheTenantAsLessorIsNot()
    {
        Assert.Equal(
            [new ContractProblem(ContractField.Codebtors, ContractRefusal.TenantIsCodebtor, Row: 1)],
            Problems(Valid() with { CodebtorIds = [Guid.NewGuid(), Juan] }));
        Assert.Empty(Problems(Valid() with { LessorIds = [Juan] }));
    }

    /// <summary>Spec Test 114: an end equal to the start is refused.</summary>
    [Fact]
    public void AnEndEqualToTheStart_IsRefused()
    {
        var start = new DateOnly(2026, 12, 1);

        Assert.Equal(
            [new ContractProblem(ContractField.EndDate, ContractRefusal.EndBeforeStart)],
            Problems(Valid() with { StartDate = start, NominalEndDate = start }));
        Assert.Empty(Problems(Valid() with { StartDate = start, NominalEndDate = start.AddDays(1) }));
    }

    /// <summary>Spec Test 114: fifty years exactly passes, a day more is refused naming the ceiling.</summary>
    [Fact]
    public void AnEndPastFiftyYears_IsRefused_AndFiftyExactlyPasses()
    {
        var start = new DateOnly(2026, 12, 1);

        Assert.Empty(Problems(Valid() with { StartDate = start, NominalEndDate = start.AddYears(50) }));
        Assert.Equal(
            [new ContractProblem(ContractField.EndDate, ContractRefusal.EndPastLegalCeiling)],
            Problems(Valid() with { StartDate = start, NominalEndDate = start.AddYears(50).AddDays(1) }));
        Assert.Contains("50 años", ContractRefusal.EndPastLegalCeiling.OperatorText(), StringComparison.Ordinal);
    }

    /// <summary>A start in the calendar's last years has no ceiling to pass, and checking it does not overflow.</summary>
    [Fact]
    public void AStartNearTheCalendarsEnd_DoesNotOverflow()
    {
        Assert.Empty(Problems(Valid() with { StartDate = new DateOnly(9999, 12, 1), NominalEndDate = new DateOnly(9999, 12, 31) }));
    }

    /// <summary>Spec Test 116: a fee plan amount with cents is refused with its own text; whole pesos pass.</summary>
    [Fact]
    public void AFeePlanAmountWithCents_IsRefused_WithItsOwnText()
    {
        Assert.Equal(
            [new ContractProblem(ContractField.FeePlan, ContractRefusal.FeePlanNotWholePesos)],
            Problems(Valid() with { FeePlanAmount = 120_000.50m, FeePlanInstalments = 3 }));
        Assert.Empty(Problems(Valid() with { FeePlanAmount = 120_000m, FeePlanInstalments = 3 }));
        Assert.NotEqual(ContractRefusal.FeePlanInvalid.OperatorText(), ContractRefusal.FeePlanNotWholePesos.OperatorText());
    }

    /// <summary>Money problems are all said, not only the first.</summary>
    [Fact]
    public void EveryMoneyProblem_IsSaid()
    {
        var draft = Valid() with { MonthlyRent = 0m, DueDay = 40, HonorariosPercentage = 120m };

        Assert.Equal(
            [ContractField.Rent, ContractField.DueDay, ContractField.Honorarios],
            Problems(draft).Select(p => p.Field));
    }

    /// <summary>Spec Test 115: a notice dated before the start is refused; on the start date it passes.</summary>
    [Fact]
    public void ANoticeBeforeTheStart_IsRefused_AndOnTheStartItPasses()
    {
        var start = new DateOnly(2026, 7, 1);

        Assert.Equal(
            ContractRefusal.NoticeBeforeStart,
            ContractLifecycleRules.CheckNotice(ContractStatus.Active, start, new DateOnly(2026, 6, 15), new DateOnly(2026, 9, 30), Today));
        Assert.Null(ContractLifecycleRules.CheckNotice(ContractStatus.Active, start, start, new DateOnly(2026, 9, 30), Today));
    }
}
