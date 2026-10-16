using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// Voiding a payment (cobranza-y-recibos spec Tests 22, 23, 24, 27; design D2, section 7.5 and
/// spec Decision 45). The planner is pure: it offers the annulments and the corrections and the
/// tests append them to an account the way the desk will. The database half of the window is Test 72,
/// in a later slice; here the same figures are checked in the domain, from the constants.
/// </summary>
public class VoidingPlannerTests
{
    private static readonly DateOnly August = CollectionPlannerTests.Scenario.August;
    private static readonly DateOnly Today = CollectionPlannerTests.Scenario.Today;
    private static readonly RecargoTerms Terms = CollectionPlannerTests.Scenario.Terms;

    /// <summary>The collection record the desk would store for a payment the planner offered.</summary>
    internal static CollectionRecord RecordOf(
        ContractAccount account, IReadOnlyList<AccountMovement> written, CollectionPreview preview) =>
        new(
            Guid.NewGuid(),
            account.Id,
            preview.Period!.Value,
            preview.ReceivedOn,
            written.Single(m => m.Kind == MovementKind.Payment).Id,
            written.SingleOrDefault(m => m.Kind == MovementKind.RecargoCharged)?.Id,
            IsAnnulled: false);

    internal static ReciboPair PairOf(CollectionPreview preview) =>
        ReciboPair.Issue(
            31_001,
            preview.Period!.Value,
            preview.ReceivedOn,
            CollectionPlannerTests.Scenario.Header(),
            new ReciboDraftPair(preview.Locatario!, preview.Propietario!));

    private sealed record Paid(
        Contract Contract,
        ContractAccount Account,
        IReadOnlyList<AccountMovement> Written,
        CollectionPreview Preview,
        CollectionRecord Record,
        ReciboPair Pair);

    /// <summary>August paid on the 12th of September, 330,000 of recargo included.</summary>
    private static Paid LatePayment()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract();
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, Today);
        var preview = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: August);
        var written = CollectionPlanner.MovementsToWrite(account, contract, preview);
        CollectionPlannerTests.Scenario.Apply(account, written);

        return new Paid(contract, account, written, preview, RecordOf(account, written, preview), PairOf(preview));
    }

    private static VoidingPreview Void(
        Paid paid,
        string? reason = "Cobrado a otro locatario",
        DateOnly? today = null,
        UserRole role = UserRole.Empleado,
        DateOnly? recordedOn = null,
        CollectionRecord? record = null,
        IReadOnlyList<Recibo>? pair = null) =>
        VoidingPlanner.Plan(
            paid.Account,
            record ?? paid.Record,
            pair ?? paid.Pair.Recibos,
            reason!,
            today ?? Today.AddDays(2),
            Terms,
            role,
            recordedOn ?? Today);

    // Test 22
    [Fact]
    public void Voiding_AppendsACorrectionOfThePayment_AndOfTheRecargo_EachNamingItsMovement()
    {
        var paid = LatePayment();
        var payment = paid.Written.Single(m => m.Kind == MovementKind.Payment);
        var recargo = paid.Written.Single(m => m.Kind == MovementKind.RecargoCharged);

        var preview = Void(paid);

        Assert.Null(preview.Refusal);
        Assert.Equal(2, preview.Corrections.Count);
        var reversesPayment = Assert.Single(preview.Corrections, c => c.CorrectsMovementId == payment.Id);
        var reversesRecargo = Assert.Single(preview.Corrections, c => c.CorrectsMovementId == recargo.Id);
        Assert.Equal(MovementKind.Correction, reversesPayment.Kind);
        Assert.Equal(830_000m, reversesPayment.Amount);
        Assert.Equal(-330_000m, reversesRecargo.Amount);
        Assert.All(preview.Corrections, c => Assert.Equal(August, c.Period));
    }

    // Test 22
    [Fact]
    public void AfterVoiding_ThePeriodIsUnpaidAgain_AndProjectsRecargoAgain()
    {
        var paid = LatePayment();
        var voidingDay = Today.AddDays(2);
        Assert.Equal(0m, paid.Account.PeriodNet(August, voidingDay));
        Assert.True(paid.Account.HasLivePaymentFor(August));

        var preview = Void(paid);
        CollectionPlannerTests.Scenario.Apply(paid.Account, preview.Corrections);

        Assert.False(paid.Account.HasLivePaymentFor(August));
        Assert.Equal(500_000m, paid.Account.PeriodNet(August, voidingDay));

        // August fell due on 10 August: 35 days late on the 14th of September.
        Assert.Equal(350_000m, paid.Account.RecargoProjectedFor(August, voidingDay, Terms));
        // July 660,000 + August 350,000 + September 40,000 of recargo on 1,500,000 of rent.
        Assert.Equal(2_550_000m, paid.Account.AmountOwed(voidingDay, Terms));
    }

    [Fact]
    public void ThePreview_ShowsTheAmountOwedAfterTheVoiding_AndTheAccountIsLeftUntouched()
    {
        var paid = LatePayment();
        var movementsBefore = paid.Account.Movements.Count;

        var preview = Void(paid);

        Assert.Equal(2_550_000m, preview.ResultingAmountOwed);
        Assert.Equal(August, preview.Period);
        Assert.Equal(movementsBefore, paid.Account.Movements.Count);
    }

    // R3-any-correction-voids-payment: any Correction naming a payment voids it, so the planner writes full reversals only.
    [Fact]
    public void EveryCorrection_ReversesItsMovementInFull()
    {
        var paid = LatePayment();

        var preview = Void(paid);

        foreach (var correction in preview.Corrections)
        {
            var target = paid.Account.Movements.Single(m => m.Id == correction.CorrectsMovementId);
            Assert.Equal(-target.Amount, correction.Amount);
        }

        Assert.DoesNotContain(
            typeof(VoidingPlanner).GetMethod(nameof(VoidingPlanner.Plan))!.GetParameters(),
            p => p.Name!.Contains("amount", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void APaymentWithNoRecargo_IsReversedByOneCorrectionOnly()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract();
        var dueDay = new DateOnly(2026, 9, 10);
        var account = CollectionPlannerTests.Scenario.AccountFor(contract, dueDay);
        var preview = CollectionPlannerTests.Scenario.PlanExact(account, contract, named: CollectionPlannerTests.Scenario.September, today: dueDay);
        var written = CollectionPlanner.MovementsToWrite(account, contract, preview);
        CollectionPlannerTests.Scenario.Apply(account, written);

        var voiding = VoidingPlanner.Plan(
            account, RecordOf(account, written, preview), PairOf(preview).Recibos,
            "Error de carga", dueDay, Terms, UserRole.Empleado, recordedOn: dueDay);

        var correction = Assert.Single(voiding.Corrections);
        Assert.Equal(500_000m, correction.Amount);
    }

    // Test 23
    [Fact]
    public void VoidingEitherRecibo_AnnulsBoth()
    {
        var paid = LatePayment();

        var forward = Void(paid);
        var reversed = Void(paid, pair: [.. paid.Pair.Recibos.Reverse()]);

        Assert.Equal([31_001L, 31_002L], forward.Annulments.Select(a => a.ReciboNumber).Order());
        Assert.Equal([31_001L, 31_002L], reversed.Annulments.Select(a => a.ReciboNumber).Order());
        Assert.All(forward.Annulments, a => Assert.Equal("Cobrado a otro locatario", a.Reason));
    }

    // Test 24
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AVoiding_NeedsAReason(string? reason)
    {
        var paid = LatePayment();

        var preview = Void(paid, reason);

        Assert.Equal(VoidingRefusal.ReasonRequired, preview.Refusal);
        Assert.Empty(preview.Corrections);
        Assert.Empty(preview.Annulments);
    }

    [Fact]
    public void TheReason_IsTrimmed()
    {
        var preview = Void(LatePayment(), reason: "  Error de carga  ");

        Assert.All(preview.Annulments, a => Assert.Equal("Error de carga", a.Reason));
    }

    // Test 24
    [Fact]
    public void AReciboAlreadyAnnulled_IsRefused()
    {
        var paid = LatePayment();

        var preview = Void(paid, record: paid.Record with { IsAnnulled = true });

        Assert.Equal(VoidingRefusal.AlreadyAnnulled, preview.Refusal);
        Assert.Empty(preview.Corrections);
    }

    // Test 24: even when nobody flagged the record, a payment a correction already names is voided.
    [Fact]
    public void APaymentAlreadyReversedOnTheLedger_IsRefusedAsAlreadyAnnulled()
    {
        var paid = LatePayment();
        CollectionPlannerTests.Scenario.Apply(paid.Account, Void(paid).Corrections);

        var again = Void(paid);

        Assert.Equal(VoidingRefusal.AlreadyAnnulled, again.Refusal);
    }

    // Test 27
    [Fact]
    public void ABalanceReadForADateBeforeTheVoiding_StillShowsThePayment()
    {
        var paid = LatePayment();
        var voidingDay = Today.AddDays(2);
        var beforeVoiding = paid.Account.LedgerBalance(Today.AddDays(1));
        var paymentOnTheLedger = paid.Written.Single(m => m.Kind == MovementKind.Payment).Amount;

        CollectionPlannerTests.Scenario.Apply(paid.Account, Void(paid).Corrections);

        // The payment is still on the ledger on the day before the voiding; the corrections start on the voiding day.
        Assert.Equal(beforeVoiding, paid.Account.LedgerBalance(Today.AddDays(1)));
        Assert.Equal(0m, paid.Account.PeriodNet(August, Today.AddDays(1)));
        Assert.Contains(paid.Account.Movements, m => m.Amount == paymentOnTheLedger && m.OccurredOn <= Today.AddDays(1));
        Assert.Equal(beforeVoiding + 500_000m, paid.Account.LedgerBalance(voidingDay));
    }

    // Test 27, D2
    [Fact]
    public void TheVoidingOfAnEarlyPayment_IsDatedOnTheDueDate_NeverBeforeThePayment()
    {
        var contract = CollectionPlannerTests.Scenario.NewContract(start: CollectionPlannerTests.Scenario.September);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        var received = new DateOnly(2026, 9, 2);
        var preview = CollectionPlannerTests.Scenario.PlanExact(
            account, contract, named: CollectionPlannerTests.Scenario.September, today: received);
        var written = CollectionPlanner.MovementsToWrite(account, contract, preview);
        CollectionPlannerTests.Scenario.Apply(account, written);
        var payment = written.Single(m => m.Kind == MovementKind.Payment);
        var dueDate = new DateOnly(2026, 9, 10);
        Assert.Equal(dueDate, payment.OccurredOn);

        var voiding = VoidingPlanner.Plan(
            account, RecordOf(account, written, preview), PairOf(preview).Recibos,
            "Error de carga", new DateOnly(2026, 9, 3), Terms, UserRole.Empleado, recordedOn: received);
        CollectionPlannerTests.Scenario.Apply(account, voiding.Corrections);

        Assert.All(voiding.Corrections, c => Assert.True(c.OccurredOn >= payment.OccurredOn));
        Assert.All(voiding.Corrections, c => Assert.Equal(dueDate, c.OccurredOn));
        Assert.Equal(0m, account.LedgerBalance(new DateOnly(2026, 9, 9)));
        Assert.Equal(500_000m, account.LedgerBalance(dueDate));
    }

    // The window (spec Decision 45), unnumbered: the same figures the database repeats in Test 72.
    [Theory]
    [InlineData(UserRole.Empleado, 0, null)]
    [InlineData(UserRole.Empleado, 7, null)]
    [InlineData(UserRole.Empleado, 8, VoidingRefusal.AdminOnly)]
    [InlineData(UserRole.Empleado, 30, VoidingRefusal.AdminOnly)]
    [InlineData(UserRole.Empleado, 31, VoidingRefusal.WindowClosed)]
    [InlineData(UserRole.Admin, 7, null)]
    [InlineData(UserRole.Admin, 8, null)]
    [InlineData(UserRole.Admin, 30, null)]
    [InlineData(UserRole.Admin, 31, VoidingRefusal.WindowClosed)]
    public void TheVoidingWindow_DependsOnTheAgeOfTheCollectionAndTheRole(UserRole role, int ageInDays, VoidingRefusal? expected)
    {
        var paid = LatePayment();
        var recordedOn = Today;

        var preview = Void(paid, role: role, today: recordedOn.AddDays(ageInDays), recordedOn: recordedOn);

        Assert.Equal(expected, preview.Refusal);

        if (expected is not null)
        {
            Assert.Empty(preview.Corrections);
            Assert.Empty(preview.Annulments);
        }
    }

    [Fact]
    public void TheWindowFigures_AreWrittenOnceInCollectionRules()
    {
        Assert.Equal(7, CollectionRules.VoidingFreeWindowDays);
        Assert.Equal(30, CollectionRules.VoidingAdminWindowDays);
    }

    [Fact]
    public void ACollectionRecordedInTheFuture_IsABug_NotARefusal()
    {
        var paid = LatePayment();

        Assert.Throws<ArgumentOutOfRangeException>(() => Void(paid, today: Today, recordedOn: Today.AddDays(1)));
    }

    [Fact]
    public void ARecordOfAnotherAccount_IsRefusedAsABug()
    {
        var paid = LatePayment();

        Assert.Throws<ArgumentException>(() => Void(paid, record: paid.Record with { AccountId = Guid.NewGuid() }));
    }

    [Fact]
    public void APaymentTheAccountDoesNotHold_IsRefusedAsABug()
    {
        var paid = LatePayment();

        Assert.Throws<ArgumentException>(() => Void(paid, record: paid.Record with { PaymentMovementId = Guid.NewGuid() }));
    }

    [Fact]
    public void APairThatIsNotTheTwoRecibosOfThisPayment_IsRefusedAsABug()
    {
        var paid = LatePayment();

        Assert.Throws<ArgumentException>(() => Void(paid, pair: [paid.Pair.Locatario]));
        Assert.Throws<ArgumentException>(() => Void(paid, pair: [paid.Pair.Locatario, paid.Pair.Locatario]));
    }

    [Fact]
    public void EveryVoidingRefusal_HasOperatorText()
    {
        foreach (var refusal in Enum.GetValues<VoidingRefusal>())
        {
            Assert.False(string.IsNullOrWhiteSpace(refusal.OperatorText()), refusal.ToString());
        }
    }

    [Fact]
    public void APlan_CarriesTheRoleItWasMadeFor_RefusedOrNot()
    {
        var paid = LatePayment();

        Assert.Equal(UserRole.Admin, Void(paid, role: UserRole.Admin).ActingRole);
        Assert.Equal(UserRole.Empleado, Void(paid, reason: " ", role: UserRole.Empleado).ActingRole);
    }
}
