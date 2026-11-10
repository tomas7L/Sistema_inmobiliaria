using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Domain.Tests;

/// <summary>gestion-de-alquileres spec Requirements 23 and 24, design §2.3 to §2.5, on the domain alone.</summary>
public class ContractCancellationTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2027, 12, 31);
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static Contract NewContract(ContractualFeePlan? plan = null) =>
        new(Guid.NewGuid(), Start, End, 450_000m, [new UnitShare(Guid.NewGuid(), 100m)], 8m, contractualFeePlan: plan);

    private static ContractAccount AccountOf(Contract contract) => new(Guid.NewGuid(), contract.Id);

    private static AccountMovement Accrue(ContractAccount account, DateOnly period)
    {
        var movement = AccountMovement.RentAccrual(Guid.NewGuid(), 450_000m, period, period.AddDays(9));
        account.Append(movement);
        return movement;
    }

    private static CancellationCollection Collection(ContractAccount account, DateOnly period, int age, bool annulled = false, int firstRecibo = 31001, decimal amount = 450_000m)
    {
        var payment = AccountMovement.Payment(Guid.NewGuid(), -amount, period, Today.AddDays(-age));
        account.Append(payment);
        return new CancellationCollection(payment.Id, Today.AddDays(-age), [firstRecibo, firstRecibo + 1], annulled);
    }

    private static decimal Balance(ContractAccount account, ContractCancellationPlan plan) =>
        account.Movements.Sum(m => m.Amount) + plan.Corrections.Sum(c => c.Amount);

    // ---- The plan --------------------------------------------------------------------------------------------------

    [Fact]
    public void UnpaidMonths_AreEachReversed_LeavingZero()
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        var first = Accrue(account, new DateOnly(2026, 9, 1));
        var second = Accrue(account, new DateOnly(2026, 10, 1));

        var plan = ContractCancellation.Plan(account, contract, [], "Inquilino equivocado", Today);

        Assert.Null(plan.Refusal);
        Assert.Equal(2, plan.Corrections.Count);
        Assert.Contains(plan.Corrections, c => c.CorrectsMovementId == first.Id && c.Amount == -450_000m && c.Period == first.Period);
        Assert.Contains(plan.Corrections, c => c.CorrectsMovementId == second.Id && c.Amount == -450_000m && c.Period == second.Period);
        Assert.All(plan.Corrections, c => Assert.Equal(MovementKind.Correction, c.Kind));
        Assert.Equal(0m, Balance(account, plan));
    }

    /// <summary>
    /// gestion-de-alquileres spec Test 92, cancellation half: a late adjustment's difference not yet due is reversed too, on its own
    /// date, so the balance shows no credit on any day before it falls due.
    /// </summary>
    [Fact]
    public void Test92_ADifferenceNotYetDue_IsReversedOnItsOwnDate_LeavingNoCreditOnAnyDay()
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        var december = new DateOnly(2026, 12, 1);
        var dueOn = new DateOnly(2026, 12, 10);
        var charge = AccountMovement.AdjustmentDifference(Guid.NewGuid(), 300_000m, december, dueOn);
        account.Append(charge);

        var plan = ContractCancellation.Plan(account, contract, [], "Cargado por error", Today);

        var reversal = Assert.Single(plan.Corrections, c => c.CorrectsMovementId == charge.Id);
        Assert.Equal(-300_000m, reversal.Amount);
        Assert.Equal(dueOn, reversal.OccurredOn);
        Assert.Equal(december, reversal.Period);
        foreach (var correction in plan.Corrections)
        {
            account.Append(correction);
        }

        for (var day = Today; day <= dueOn.AddDays(1); day = day.AddDays(1))
        {
            Assert.True(account.LedgerBalance(day) >= 0m, $"A credit on {day}.");
        }

        Assert.Equal(0m, account.LedgerBalance(dueOn));
    }

    [Fact]
    public void EveryChargeKind_IsReversed_ForItsNetAfterEarlierCorrections()
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        var period = new DateOnly(2026, 9, 1);
        var rent = Accrue(account, period);
        var fee = AccountMovement.ContractualFeeInstalment(Guid.NewGuid(), 5_000m, period, period.AddDays(9));
        var earlier = AccountMovement.Correction(Guid.NewGuid(), -50_000m, Today, rent.Id, period);
        var opening = AccountMovement.OpeningBalance(Guid.NewGuid(), 70_000m, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1));
        var frozen = AccountMovement.RecargoFrozen(Guid.NewGuid(), 9_000m, new DateOnly(2026, 8, 20));
        var recargo = AccountMovement.RecargoCharged(Guid.NewGuid(), 1_000m, period, period.AddDays(15));
        account.Append(recargo);
        account.Append(fee);
        account.Append(earlier);
        account.Append(opening);
        account.Append(frozen);

        var plan = ContractCancellation.Plan(account, contract, [], "Error", Today);

        Assert.Contains(plan.Corrections, c => c.CorrectsMovementId == rent.Id && c.Amount == -400_000m);
        Assert.Contains(plan.Corrections, c => c.CorrectsMovementId == fee.Id && c.Amount == -5_000m);
        Assert.Contains(plan.Corrections, c => c.CorrectsMovementId == recargo.Id && c.Amount == -1_000m);
        Assert.Contains(plan.Corrections, c => c.CorrectsMovementId == opening.Id && c.Amount == -70_000m && c.Period == opening.Period);
        Assert.Contains(plan.Corrections, c => c.CorrectsMovementId == frozen.Id && c.Amount == -9_000m && c.Period is null);
        Assert.Equal(0m, Balance(account, plan));
    }

    /// <summary>A correction dated before what it reverses would read as credit (design D10): a voided early payment leaves rent dated ahead.</summary>
    [Fact]
    public void AReversal_IsNeverDatedBeforeWhatItReverses()
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        var late = new DateOnly(2026, 11, 1);
        var rent = AccountMovement.RentAccrual(Guid.NewGuid(), 450_000m, late, new DateOnly(2026, 11, 30));
        var old = Accrue(account, new DateOnly(2026, 9, 1));
        account.Append(rent);

        var plan = ContractCancellation.Plan(account, contract, [], "Error", Today);

        Assert.Equal(new DateOnly(2026, 11, 30), plan.Corrections.Single(c => c.CorrectsMovementId == rent.Id).OccurredOn);
        Assert.Equal(Today, plan.Corrections.Single(c => c.CorrectsMovementId == old.Id).OccurredOn);
    }

    [Fact]
    public void AContractWithNoMovements_CanBeCancelled_WithNothingToReverse()
    {
        var contract = NewContract();

        var plan = ContractCancellation.Plan(AccountOf(contract), contract, [], "Error", Today);

        Assert.Null(plan.Refusal);
        Assert.Empty(plan.Corrections);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void AnEmptyReason_IsRefused(string reason)
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        Accrue(account, new DateOnly(2026, 9, 1));

        var plan = ContractCancellation.Plan(account, contract, [], reason, Today);

        Assert.Equal(ContractRefusal.ReasonMissing, plan.Refusal);
        Assert.Empty(plan.Corrections);
    }

    /// <summary>Spec Requirement 23: only spaces count as empty, and no minimum length applies.</summary>
    [Fact]
    public void ASingleLetter_IsAReason()
    {
        var contract = NewContract();

        Assert.Null(ContractCancellation.Plan(AccountOf(contract), contract, [], "x", Today).Refusal);
    }

    [Fact]
    public void AnAlreadyCancelledContract_IsRefused()
    {
        var contract = NewContract();
        contract.Cancel(Today);

        var plan = ContractCancellation.Plan(AccountOf(contract), contract, [], "Error", Today);

        Assert.Equal(ContractRefusal.ContractCancelled, plan.Refusal);
    }

    /// <summary>Spec Decision 45's windows through the shared function: day 7 anyone, days 8 to 30 an Admin, day 31 nobody.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(30, true)]
    public void AVoidablePayment_Blocks_NamingItsRecibos_AndWhetherAnAdminIsNeeded(int age, bool adminNeeded)
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        var period = new DateOnly(2026, 9, 1);
        Accrue(account, period);
        Accrue(account, new DateOnly(2026, 10, 1));
        var collection = Collection(account, period, age);

        var plan = ContractCancellation.Plan(account, contract, [collection], "Error", Today);

        Assert.Equal(ContractRefusal.PaymentMustBeVoidedFirst, plan.Refusal);
        Assert.Equal([31001, 31002], plan.RecibosToVoid);
        Assert.Equal(adminNeeded, plan.OnlyAnAdminCanVoid);
        Assert.Empty(plan.Corrections);
    }

    /// <summary>An Admin is needed as soon as one of the payments needs one, since the operator has to void them all.</summary>
    [Fact]
    public void TwoVoidablePayments_NameEveryRecibo_AndAdminWhenOneNeedsIt()
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        var first = new DateOnly(2026, 9, 1);
        var second = new DateOnly(2026, 10, 1);
        Accrue(account, first);
        Accrue(account, second);

        var plan = ContractCancellation.Plan(
            account,
            contract,
            [Collection(account, first, 3), Collection(account, second, 12, firstRecibo: 31003)],
            "Error",
            Today);

        Assert.Equal([31001, 31002, 31003, 31004], plan.RecibosToVoid);
        Assert.True(plan.OnlyAnAdminCanVoid);
    }

    [Fact]
    public void APaymentPastTheWindow_StaysWithItsRecibos_AndOnlyTheUnpaidMonthIsReversed()
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        var paid = new DateOnly(2026, 8, 1);
        var unpaid = new DateOnly(2026, 9, 1);
        var paidRent = Accrue(account, paid);
        var unpaidRent = Accrue(account, unpaid);
        var collection = Collection(account, paid, 31);

        var plan = ContractCancellation.Plan(account, contract, [collection], "Error", Today);

        Assert.Null(plan.Refusal);
        Assert.Equal(unpaidRent.Id, Assert.Single(plan.Corrections).CorrectsMovementId);
        Assert.DoesNotContain(plan.Corrections, c => c.CorrectsMovementId == paidRent.Id || c.CorrectsMovementId == collection.PaymentMovementId);
        Assert.Equal(450_000m, plan.RetainedAmount);
        Assert.Equal([31001, 31002], plan.RetainedRecibos);
        Assert.Equal(0m, Balance(account, plan));
    }

    /// <summary>A voided payment no longer stands: nothing about it blocks, and the rent it had covered is owed and reversed.</summary>
    [Fact]
    public void AVoidedPayment_DoesNotBlock_AndItsRentIsReversed()
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        var period = new DateOnly(2026, 9, 1);
        var rent = Accrue(account, period);
        var collection = Collection(account, period, 2, annulled: true);
        account.Append(AccountMovement.Correction(Guid.NewGuid(), 450_000m, Today, collection.PaymentMovementId, period));

        var plan = ContractCancellation.Plan(account, contract, [collection], "Error", Today);

        Assert.Null(plan.Refusal);
        Assert.Equal(rent.Id, Assert.Single(plan.Corrections).CorrectsMovementId);
        Assert.Empty(plan.RetainedRecibos);
        Assert.Equal(0m, plan.RetainedAmount);
        Assert.Equal(0m, Balance(account, plan));
    }

    /// <summary>Reversing a whole charge on a part-paid month would leave a credit. The month is taken to zero, not past it.</summary>
    [Fact]
    public void APartPaidMonth_IsReversedForWhatIsLeft_AndNoMore()
    {
        var contract = NewContract();
        var account = AccountOf(contract);
        var period = new DateOnly(2026, 9, 1);
        var rent = Accrue(account, period);
        var collection = Collection(account, period, 40, amount: 200_000m);

        var plan = ContractCancellation.Plan(account, contract, [collection], "Error", Today);

        var correction = Assert.Single(plan.Corrections);
        Assert.Equal(rent.Id, correction.CorrectsMovementId);
        Assert.Equal(-250_000m, correction.Amount);
        Assert.Equal(0m, Balance(account, plan));
    }

    // ---- The voiding window, one shared function (design D11) --------------------------------------------------------

    [Theory]
    [InlineData(0, VoidingWindow.Free)]
    [InlineData(7, VoidingWindow.Free)]
    [InlineData(8, VoidingWindow.AdminOnly)]
    [InlineData(30, VoidingWindow.AdminOnly)]
    [InlineData(31, VoidingWindow.Closed)]
    public void TheVoidingWindow_ByAge(int age, VoidingWindow window) => Assert.Equal(window, CollectionRules.VoidingWindowOf(age));

    // ---- Accrual (Test 73) -------------------------------------------------------------------------------------------

    /// <summary>The end date alone would still produce every unwritten period due before it. Spec Test 73 must fail if it governs.</summary>
    [Fact]
    public void ACancelledContract_ProducesNoPeriod_EvenOnesDueBeforeItEnded()
    {
        var contract = NewContract(new ContractualFeePlan(5_000m, 3));
        Assert.NotEmpty(AccrualSchedule.PeriodsDue(contract, Today));

        contract.Cancel(Today);

        Assert.Equal(Today, contract.ActualEndDate);
        Assert.Empty(AccrualSchedule.PeriodsDue(contract, Today));
        Assert.Empty(AccrualSchedule.PeriodsBegun(contract, Today));
        Assert.Empty(AccrualSchedule.PeriodsDue(contract, Today.AddYears(1)));
        Assert.Null(AccrualSchedule.InstalmentAmountFor(contract, Start));
    }

    [Fact]
    public void AnOrdinaryEnd_StillAccruesWhatFellDueBeforeIt()
    {
        var contract = NewContract();
        contract.End(EndReason.Expiry, new DateOnly(2026, 3, 15));

        Assert.Equal(3, AccrualSchedule.PeriodsDue(contract, Today).Count);
    }

    // ---- The contract's own transitions ------------------------------------------------------------------------------

    [Theory]
    [InlineData(ContractStatus.Active)]
    [InlineData(ContractStatus.PendingTermination)]
    public void Cancel_FromAnyLiveState_EndsItAsLoadedByMistake(ContractStatus status)
    {
        var contract = NewContract();
        if (status == ContractStatus.PendingTermination)
        {
            contract.GiveNotice(Today, Today);
        }

        contract.Cancel(Today);

        Assert.Equal(ContractStatus.Ended, contract.Status);
        Assert.Equal(EndReason.LoadedByMistake, contract.EndReason);
        Assert.Equal(Today, contract.ActualEndDate);
    }

    /// <summary>Spec Requirement 23: the cancellation replaces an earlier reason, and an earlier end date stays.</summary>
    [Fact]
    public void Cancel_AnEndedContract_ReplacesTheReason_AndKeepsAnEarlierEnd()
    {
        var contract = NewContract();
        var ended = new DateOnly(2026, 5, 20);
        contract.End(EndReason.Expiry, ended);

        contract.Cancel(Today);

        Assert.Equal(EndReason.LoadedByMistake, contract.EndReason);
        Assert.Equal(ended, contract.ActualEndDate);
    }

    [Fact]
    public void Cancel_Twice_Throws()
    {
        var contract = NewContract();
        contract.Cancel(Today);

        Assert.Throws<InvalidOperationException>(() => contract.Cancel(Today.AddDays(1)));
        Assert.Equal(Today, contract.ActualEndDate);
    }

    /// <summary>Only cancelling can set it, or an ordinary end could erase accrual without reversing anything.</summary>
    [Fact]
    public void End_WithLoadedByMistake_Throws()
    {
        var contract = NewContract();

        Assert.Throws<InvalidOperationException>(() => contract.End(EndReason.LoadedByMistake, Today));
        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void UndoNotice_ReturnsToActive_WithBothDatesEmpty()
    {
        var contract = NewContract();
        contract.GiveNotice(Today, Today.AddDays(30));

        contract.UndoNotice();

        Assert.Equal(ContractStatus.Active, contract.Status);
        Assert.Null(contract.NoticeGivenDate);
        Assert.Null(contract.PlannedMoveOutDate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndoNotice_FromAnyOtherState_Throws(bool ended)
    {
        var contract = NewContract();
        if (ended)
        {
            contract.End(EndReason.Expiry, Today);
        }

        Assert.Throws<InvalidOperationException>(contract.UndoNotice);
    }

    [Fact]
    public void UndoEnd_ReturnsToActive_WhenNoNoticeWasGiven()
    {
        var contract = NewContract();
        contract.End(EndReason.Expiry, Today);

        contract.UndoEnd();

        Assert.Equal(ContractStatus.Active, contract.Status);
        Assert.Null(contract.ActualEndDate);
        Assert.Null(contract.EndReason);
    }

    [Fact]
    public void UndoEnd_ReturnsToPendingTermination_WhenNoticeWasGiven_KeepingIt()
    {
        var contract = NewContract();
        contract.GiveNotice(Today, Today.AddDays(30));
        contract.End(EndReason.Expiry, Today);

        contract.UndoEnd();

        Assert.Equal(ContractStatus.PendingTermination, contract.Status);
        Assert.Equal(Today, contract.NoticeGivenDate);
        Assert.Null(contract.ActualEndDate);
    }

    [Fact]
    public void UndoEnd_OfACancelledContract_Throws()
    {
        var contract = NewContract();
        contract.Cancel(Today);

        Assert.Throws<InvalidOperationException>(contract.UndoEnd);
        Assert.Equal(EndReason.LoadedByMistake, contract.EndReason);
    }

    [Fact]
    public void UndoEnd_OfAContractNotEnded_Throws() => Assert.Throws<InvalidOperationException>(NewContract().UndoEnd);

    // ---- The rules as values -----------------------------------------------------------------------------------------

    /// <summary>Spec Test 89: "Cargado por error" belongs to cancelling. The port refuses it however it arrives.</summary>
    [Fact]
    public void EndingWithLoadedByMistake_IsRefused()
    {
        Assert.Equal(
            ContractRefusal.EndReasonNotOffered,
            ContractLifecycleRules.CheckEnd(ContractStatus.Active, Start, EndReason.LoadedByMistake, Today, Today));
    }

    [Theory]
    [InlineData(ContractStatus.Active)]
    [InlineData(ContractStatus.Ended)]
    public void UndoingANotice_NeedsPendingTermination(ContractStatus status) =>
        Assert.Equal(ContractRefusal.NoNoticeToUndo, ContractLifecycleRules.CheckUndoNotice(status, "Error"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UndoingANotice_NeedsAReason(string reason) =>
        Assert.Equal(ContractRefusal.ReasonMissing, ContractLifecycleRules.CheckUndoNotice(ContractStatus.PendingTermination, reason));

    [Fact]
    public void UndoingAnEnd_ReadsItsState()
    {
        Assert.Null(ContractLifecycleRules.CheckUndoEnd(ContractStatus.Ended, EndReason.Expiry, "Error"));
        Assert.Equal(ContractRefusal.NoEndToUndo, ContractLifecycleRules.CheckUndoEnd(ContractStatus.Active, null, "Error"));
        Assert.Equal(ContractRefusal.ContractCancelled, ContractLifecycleRules.CheckUndoEnd(ContractStatus.Ended, EndReason.LoadedByMistake, "Error"));
        Assert.Equal(ContractRefusal.ReasonMissing, ContractLifecycleRules.CheckUndoEnd(ContractStatus.Ended, EndReason.Expiry, " "));
    }
}
