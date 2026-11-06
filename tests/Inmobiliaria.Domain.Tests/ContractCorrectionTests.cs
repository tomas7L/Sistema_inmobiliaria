using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-alquileres spec Requirements 9 and 10, Decision 33 and design §2.3, on the domain alone.</summary>
public class ContractCorrectionTests
{
    private static readonly DateOnly Start = new(2026, 12, 1);
    private static readonly DateOnly End = new(2028, 11, 30);
    private static readonly DateTimeOffset At = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static Contract NewContract() => new(Guid.NewGuid(), Start, End, 450_000m, [new UnitShare(Guid.NewGuid(), 100m)]);

    private static ContractAccount EmptyAccount(Contract contract) => new(Guid.NewGuid(), contract.Id);

    private static ContractAccount AccountWithMovement(Contract contract)
    {
        var account = EmptyAccount(contract);
        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 450_000m, Start, new DateOnly(2026, 12, 10)));
        return account;
    }

    private static Party Person(string last = "Pérez") => new(Guid.NewGuid(), "Juan", last);

    [Fact]
    public void Correct_BeforeAnyMovement_ChangesEveryTerm()
    {
        var contract = NewContract();
        var plan = new ContractualFeePlan(5_000m, 3);

        contract.Correct(new DateOnly(2027, 1, 1), new DateOnly(2029, 1, 31), 500_000m, 5, plan, EmptyAccount(contract));

        Assert.Equal(new DateOnly(2027, 1, 1), contract.StartDate);
        Assert.Equal(new DateOnly(2029, 1, 31), contract.NominalEndDate);
        Assert.Equal(500_000m, contract.MonthlyRent);
        Assert.Equal(5, contract.DueDay);
        Assert.Equal(plan, contract.ContractualFeePlan);
    }

    [Fact]
    public void Correct_AfterAMovement_Throws_AndChangesNothing()
    {
        var contract = NewContract();

        Assert.Throws<InvalidOperationException>(
            () => contract.Correct(new DateOnly(2027, 1, 1), End, 500_000m, 5, null, AccountWithMovement(contract)));

        Assert.Equal(Start, contract.StartDate);
        Assert.Equal(450_000m, contract.MonthlyRent);
        Assert.Equal(Contract.DefaultDueDay, contract.DueDay);
    }

    [Fact]
    public void Correct_WithAnotherContractsAccount_Throws()
    {
        var contract = NewContract();

        Assert.Throws<ArgumentException>(() => contract.Correct(Start, End, 500_000m, 5, null, EmptyAccount(NewContract())));

        Assert.Equal(450_000m, contract.MonthlyRent);
    }

    [Theory]
    [InlineData(0, 450_000)]
    [InlineData(32, 450_000)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void Correct_RefusesWhatTheConstructorRefuses(int dueDay, int rent)
    {
        var contract = NewContract();

        Assert.Throws<ArgumentException>(() => contract.Correct(Start, End, rent, dueDay, null, EmptyAccount(contract)));

        Assert.Equal(450_000m, contract.MonthlyRent);
        Assert.Equal(Contract.DefaultDueDay, contract.DueDay);
    }

    [Fact]
    public void Correct_AnEndBeforeTheStart_Throws()
    {
        var contract = NewContract();

        Assert.Throws<ArgumentException>(() => contract.Correct(End, Start, 500_000m, 10, null, EmptyAccount(contract)));

        Assert.Equal(Start, contract.StartDate);
    }

    [Fact]
    public void Correct_AnEndedContract_Throws()
    {
        var contract = NewContract();
        contract.End(EndReason.Expiry, Start);

        Assert.Throws<InvalidOperationException>(() => contract.Correct(Start, End, 500_000m, 10, null, EmptyAccount(contract)));

        Assert.Equal(450_000m, contract.MonthlyRent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8.5)]
    [InlineData(100)]
    public void TheRate_CanBeSet_FromZeroToAHundred(double rate)
    {
        var contract = NewContract();

        contract.SetHonorariosPercentage((decimal)rate);

        Assert.Equal((decimal)rate, contract.HonorariosPercentage);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void TheRate_OutOfRange_Throws_AndKeepsTheOldOne(double rate)
    {
        var contract = new Contract(Guid.NewGuid(), Start, End, 450_000m, [new UnitShare(Guid.NewGuid(), 100m)], honorariosPercentage: 8m);

        Assert.Throws<ArgumentException>(() => contract.SetHonorariosPercentage((decimal)rate));

        Assert.Equal(8m, contract.HonorariosPercentage);
    }

    [Fact]
    public void ACodeudor_CanBeRemovedWithMovements_AndNoLongerReads()
    {
        var contract = NewContract();
        var codeudor = Person();
        contract.AssignParty(codeudor, PartyRole.Codebtor);

        contract.RemoveParty(codeudor.Id, PartyRole.Codebtor, AccountWithMovement(contract), At);

        Assert.Empty(contract.Parties);
    }

    [Theory]
    [InlineData(PartyRole.Tenant)]
    [InlineData(PartyRole.Lessor)]
    public void ATenantOrLessor_CannotBeRemovedWithMovements(PartyRole role)
    {
        var contract = NewContract();
        var kept = Person();
        contract.AssignParty(kept, role);

        // A replacement is on its way, so only the movement can be what stops it.
        Assert.Throws<InvalidOperationException>(
            () => contract.RemoveParty(kept.Id, role, AccountWithMovement(contract), At, replacementFollows: true));

        Assert.Contains(contract.Parties, p => p.PartyId == kept.Id);
    }

    [Theory]
    [InlineData(PartyRole.Tenant)]
    [InlineData(PartyRole.Lessor)]
    public void TheLastTenantOrLessor_CannotBeRemoved_UnlessAReplacementFollows(PartyRole role)
    {
        var contract = NewContract();
        var only = Person();
        contract.AssignParty(only, role);

        Assert.Throws<InvalidOperationException>(() => contract.RemoveParty(only.Id, role, EmptyAccount(contract), At));
        Assert.Single(contract.Parties);

        contract.RemoveParty(only.Id, role, EmptyAccount(contract), At, replacementFollows: true);

        Assert.Empty(contract.Parties);
    }

    [Fact]
    public void OneOfTwoLessors_CanBeRemovedBeforeAnyMovement()
    {
        var contract = NewContract();
        var first = Person();
        var second = Person("Gómez");
        contract.AssignParty(first, PartyRole.Lessor);
        contract.AssignParty(second, PartyRole.Lessor);

        contract.RemoveParty(first.Id, PartyRole.Lessor, EmptyAccount(contract), At);

        Assert.Equal(second.Id, Assert.Single(contract.Parties).PartyId);
    }

    [Fact]
    public void RemovingSomeoneNotOnTheContract_Throws()
    {
        var contract = NewContract();

        Assert.Throws<InvalidOperationException>(
            () => contract.RemoveParty(Guid.NewGuid(), PartyRole.Codebtor, EmptyAccount(contract), At));
    }

    /// <summary>The key is (contract, party, role), so a second row for the same person and role could never be written.</summary>
    [Fact]
    public void AssigningARemovedPerson_RestoresTheirRow_WithoutASecondOne()
    {
        var contract = NewContract();
        var codeudor = Person();
        contract.AssignParty(codeudor, PartyRole.Codebtor);
        var row = Assert.Single(contract.Parties);
        contract.RemoveParty(codeudor.Id, PartyRole.Codebtor, EmptyAccount(contract), At);
        Assert.Equal(At, row.RemovedAt);

        contract.AssignParty(codeudor, PartyRole.Codebtor);

        Assert.Same(row, Assert.Single(contract.Parties));
        Assert.Null(row.RemovedAt);
    }

    [Fact]
    public void ATenantRemoved_LeavesRoomForAnother_AndNotForTwo()
    {
        var contract = NewContract();
        var first = Person();
        var second = Person("Gómez");
        contract.AssignParty(first, PartyRole.Tenant);
        contract.RemoveParty(first.Id, PartyRole.Tenant, EmptyAccount(contract), At, replacementFollows: true);

        contract.AssignParty(second, PartyRole.Tenant);

        Assert.Equal(second.Id, Assert.Single(contract.Parties).PartyId);
        Assert.Throws<InvalidOperationException>(() => contract.AssignParty(first, PartyRole.Tenant));
        Assert.Equal(second.Id, Assert.Single(contract.Parties).PartyId);
    }

    [Fact]
    public void AssigningSomeoneAlreadyOn_StillThrows()
    {
        var contract = NewContract();
        var codeudor = Person();
        contract.AssignParty(codeudor, PartyRole.Codebtor);

        Assert.Throws<InvalidOperationException>(() => contract.AssignParty(codeudor, PartyRole.Codebtor));
    }
}

/// <summary>The date bounds of notice and end (spec Requirement 10), kept out of <see cref="Contract"/> because Domain has no clock.</summary>
public class ContractLifecycleRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Theory]
    [InlineData("2026-11-15", "2026-11-15")]
    [InlineData("2026-10-10", "2026-12-31")]
    public void Notice_NotAfterTodayWithAMoveOutNotBeforeIt_IsAllowed(string notice, string moveOut)
    {
        Assert.Null(ContractLifecycleRules.CheckNotice(ContractStatus.Active, DateOnly.Parse(notice), DateOnly.Parse(moveOut), Today));
    }

    [Fact]
    public void Notice_AfterToday_IsRefused()
    {
        Assert.Equal(
            ContractRefusal.NoticeAfterToday,
            ContractLifecycleRules.CheckNotice(ContractStatus.Active, Today.AddDays(1), Today.AddDays(5), Today));
    }

    [Fact]
    public void AMoveOutBeforeNotice_IsRefused()
    {
        Assert.Equal(
            ContractRefusal.MoveOutBeforeNotice,
            ContractLifecycleRules.CheckNotice(ContractStatus.Active, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1), Today));
    }

    [Theory]
    [InlineData(ContractStatus.PendingTermination)]
    [InlineData(ContractStatus.Ended)]
    public void Notice_FromAnyStateButActive_IsRefused(ContractStatus status)
    {
        Assert.Equal(ContractRefusal.NoticeNeedsActive, ContractLifecycleRules.CheckNotice(status, Today, Today, Today));
    }

    [Theory]
    [InlineData(ContractStatus.Active)]
    [InlineData(ContractStatus.PendingTermination)]
    public void End_WithAReasonAndADateWithinBounds_IsAllowed(ContractStatus status)
    {
        Assert.Null(ContractLifecycleRules.CheckEnd(status, Start, EndReason.Expiry, Today, Today));
        Assert.Null(ContractLifecycleRules.CheckEnd(status, Start, EndReason.Expiry, Start, Today));
    }

    [Fact]
    public void End_WithoutAReason_IsRefused()
    {
        Assert.Equal(ContractRefusal.EndReasonMissing, ContractLifecycleRules.CheckEnd(ContractStatus.Active, Start, null, Today, Today));
    }

    [Fact]
    public void End_AfterToday_IsRefused()
    {
        Assert.Equal(ContractRefusal.EndAfterToday, ContractLifecycleRules.CheckEnd(ContractStatus.Active, Start, EndReason.Expiry, Today.AddDays(1), Today));
    }

    [Fact]
    public void End_BeforeTheStart_IsRefused()
    {
        Assert.Equal(ContractRefusal.EndBeforeContractStart, ContractLifecycleRules.CheckEnd(ContractStatus.Active, Start, EndReason.Expiry, Start.AddDays(-1), Today));
    }

    [Fact]
    public void End_WhenAlreadyEnded_IsRefused()
    {
        Assert.Equal(ContractRefusal.ContractEnded, ContractLifecycleRules.CheckEnd(ContractStatus.Ended, Start, EndReason.Expiry, Today, Today));
    }
}

/// <summary>A correction takes the creation form's checks, except that it cannot be refused for a start date it did not change.</summary>
public class ContractDraftRulesOnACorrectionTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static ContractDraft Draft(DateOnly start) => new()
    {
        TenantId = Guid.NewGuid(),
        LessorIds = [Guid.NewGuid()],
        Shares = [new UnitShare(Guid.NewGuid(), 100m)],
        StartDate = start,
        NominalEndDate = new DateOnly(2028, 11, 30),
        MonthlyRent = 450_000m,
    };

    [Fact]
    public void AStartBeforeTheCurrentMonth_ThatDidNotChange_IsNotRefused()
    {
        var start = new DateOnly(2026, 9, 1);

        Assert.Null(ContractDraftRules.Check(Draft(start), Today, standingStart: start));
    }

    [Fact]
    public void AStartMovedBeforeTheCurrentMonth_IsRefused()
    {
        Assert.Equal(
            ContractRefusal.StartBeforeCurrentMonth,
            ContractDraftRules.Check(Draft(new DateOnly(2026, 8, 1)), Today, standingStart: new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void AStartNotOnTheFirst_ThatDidNotChange_IsNotRefused()
    {
        var start = new DateOnly(2026, 12, 15);

        Assert.Null(ContractDraftRules.Check(Draft(start), Today, standingStart: start));
    }

    [Fact]
    public void AStartMovedOffTheFirst_IsRefused()
    {
        Assert.Equal(
            ContractRefusal.StartNotOnTheFirst,
            ContractDraftRules.Check(Draft(new DateOnly(2026, 12, 15)), Today, standingStart: new DateOnly(2026, 12, 1)));
    }

    /// <summary>The form's own call is unchanged: with no standing start, a past start is refused as before.</summary>
    [Fact]
    public void WithoutAStandingStart_CreationIsAsBefore()
    {
        Assert.Equal(ContractRefusal.StartBeforeCurrentMonth, ContractDraftRules.Check(Draft(new DateOnly(2026, 9, 1)), Today));
    }
}
