using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

public class AmountOwedTests
{
    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly August = new(2026, 8, 1);
    private static readonly DateOnly JulyDue = new(2026, 7, 10);
    private static readonly DateOnly AugustDue = new(2026, 8, 10);

    private static ContractAccount NewAccount() => new(Guid.NewGuid(), Guid.NewGuid());

    private static AccountMovement Rent(decimal amount, DateOnly period, DateOnly dueOn) =>
        AccountMovement.RentAccrual(Guid.NewGuid(), amount, period, dueOn);

    /// <summary>
    /// Spec test 6, second half: the amount owed adds the projected recargo while the ledger
    /// balance reads unchanged. Two figures for the same account on the same date, and the whole
    /// point of naming them apart.
    /// </summary>
    [Fact]
    public void AmountOwed_AddsTheProjectedRecargo_WhileTheLedgerBalanceStaysPut()
    {
        var account = NewAccount();
        account.Append(Rent(500_000m, July, JulyDue));

        var asOf = new DateOnly(2026, 7, 20);

        Assert.Equal(500_000m, account.LedgerBalance(asOf));
        Assert.Equal(600_000m, account.AmountOwed(asOf, LeaseTerms));
    }

    /// <summary>Asked on the due day, the two figures agree: nothing has accrued yet.</summary>
    [Fact]
    public void OnTheDueDay_BothFiguresAgree()
    {
        var account = NewAccount();
        account.Append(Rent(500_000m, July, JulyDue));

        Assert.Equal(account.LedgerBalance(JulyDue), account.AmountOwed(JulyDue, LeaseTerms));
    }

    /// <summary>
    /// Spec test 15: each unpaid period keeps its own clock. July is 41 days late and August is
    /// 10, measured from their own due dates — not from one clock for the account.
    /// </summary>
    [Fact]
    public void EachUnpaidPeriod_CountsFromItsOwnDueDate()
    {
        var account = NewAccount();
        account.Append(Rent(500_000m, July, JulyDue));
        account.Append(Rent(500_000m, August, AugustDue));

        var asOf = new DateOnly(2026, 8, 20);

        // 41 days on July, 10 on August, at 2% of 500,000 a day.
        Assert.Equal(1_000_000m, account.LedgerBalance(asOf));
        Assert.Equal(1_000_000m + 410_000m + 100_000m, account.AmountOwed(asOf, LeaseTerms));
    }

    /// <summary>
    /// Spec test 15, second half: once a period nets to zero it stops accruing, and the others are
    /// untouched. This is what a payment will do — the payment movement belongs to the collection
    /// change, so the settling is shown here with the correction that already exists.
    /// </summary>
    [Fact]
    public void ASettledPeriod_StopsAccruing_AndTheOthersAreUnaffected()
    {
        var account = NewAccount();
        var july = Rent(500_000m, July, JulyDue);
        account.Append(july);
        account.Append(Rent(500_000m, August, AugustDue));

        account.Append(AccountMovement.Correction(
            Guid.NewGuid(), -500_000m, new DateOnly(2026, 7, 25), july.Id, July));

        var asOf = new DateOnly(2026, 8, 20);

        Assert.Equal(500_000m, account.LedgerBalance(asOf));
        Assert.Equal(600_000m, account.AmountOwed(asOf, LeaseTerms));
    }

    /// <summary>A period paid on time never contributes a surcharge at all.</summary>
    [Fact]
    public void AClearAccount_OwesNothing()
    {
        var account = NewAccount();

        var asOf = new DateOnly(2026, 12, 31);

        Assert.Equal(0m, account.LedgerBalance(asOf));
        Assert.Equal(0m, account.AmountOwed(asOf, LeaseTerms));
    }

    /// <summary>
    /// An opening balance carries no period, so no surcharge is projected on it. There is no due
    /// date to count from, and inventing one would invent a debt: the figure the agency types at
    /// go-live is what their spreadsheet already says is owed, mora included.
    /// </summary>
    [Fact]
    public void AnOpeningBalance_AccruesNoRecargo()
    {
        var account = NewAccount();
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 150_000m, July));

        var asOf = new DateOnly(2026, 12, 31);

        Assert.Equal(150_000m, account.LedgerBalance(asOf));
        Assert.Equal(150_000m, account.AmountOwed(asOf, LeaseTerms));
    }

    /// <summary>
    /// Spec test 23: a novación stops the surcharge on its signing date. Read a month later, the
    /// account reports exactly what had accrued to the signing day.
    ///
    /// July fell due on the 10th and the plan was signed on 1 August: 22 days, 220,000. That
    /// figure becomes a movement, and nothing further is projected — projecting on top of it
    /// would both run a clock that stopped and count the freeze twice.
    /// </summary>
    [Fact]
    public void ANovacion_FreezesWhatIsOwed_AndAMonthLaterItReadsTheSame()
    {
        var account = NewAccount();
        account.Append(Rent(500_000m, July, JulyDue));

        var signedOn = new DateOnly(2026, 8, 1);
        var accruedToSigning = account.AmountOwed(signedOn, LeaseTerms) - account.LedgerBalance(signedOn);
        Assert.Equal(220_000m, accruedToSigning);

        account.Append(AccountMovement.RecargoFrozen(Guid.NewGuid(), accruedToSigning, signedOn));

        var atSigning = account.AmountOwed(signedOn, LeaseTerms);
        var aMonthLater = account.AmountOwed(new DateOnly(2026, 9, 1), LeaseTerms);

        Assert.Equal(720_000m, atSigning);
        Assert.Equal(atSigning, aMonthLater);
    }

    /// <summary>
    /// Spec test 25: a date before the freeze still reads correctly. The freeze is a dated
    /// movement, so asking about an earlier day is unaffected by it — a boolean would have
    /// rewritten every past balance to whatever the freeze later turned out to be.
    /// </summary>
    [Fact]
    public void ADateBeforeTheFreeze_ReadsAsItDidThen()
    {
        var account = NewAccount();
        account.Append(Rent(500_000m, July, JulyDue));

        var signedOn = new DateOnly(2026, 8, 1);
        account.Append(AccountMovement.RecargoFrozen(Guid.NewGuid(), 220_000m, signedOn));

        var before = new DateOnly(2026, 7, 20);

        Assert.Equal(500_000m, account.LedgerBalance(before));
        Assert.Equal(600_000m, account.AmountOwed(before, LeaseTerms));
    }

    /// <summary>
    /// Spec test 24: the frozen figure is a dated movement, and no boolean carries this state.
    /// Asserted over the model rather than only over behaviour — behaviour can be right today
    /// while somebody adds the flag tomorrow.
    ///
    /// The claim is deliberately broad: no property in this namespace is a bool at all. If a
    /// legitimate boolean is ever needed here, this test is where the case for it gets made.
    /// </summary>
    [Fact]
    public void NoBooleanFlag_CarriesFrozenState()
    {
        var types = typeof(ContractAccount).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Inmobiliaria.Domain.Accounts")
            .ToList();

        Assert.NotEmpty(types);

        var flags = types
            .SelectMany(t => t.GetProperties().Select(p => (Type: t, Property: p)))
            .Where(x => (Nullable.GetUnderlyingType(x.Property.PropertyType)
                         ?? x.Property.PropertyType) == typeof(bool))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToList();

        Assert.Empty(flags);

        // And the state is carried where the specification says: a dated movement whose kind says
        // what it is.
        Assert.Contains(MovementKind.RecargoFrozen, Enum.GetValues<MovementKind>());

        var frozen = AccountMovement.RecargoFrozen(Guid.NewGuid(), 220_000m, new DateOnly(2026, 8, 1));
        Assert.Equal(220_000m, frozen.Amount);
        Assert.Equal(new DateOnly(2026, 8, 1), frozen.OccurredOn);
    }

    /// <summary>
    /// Spec test 26: a contract can be rescinded owing money. Ending a contract whose account
    /// owes 300,000 succeeds, and the account stays open.
    ///
    /// Asserted against Contract.End, which this change does NOT modify: the point is that
    /// nothing had to be added to make this work. The account is not coupled to the contract's
    /// lifecycle, so there was never a block to remove.
    /// </summary>
    [Fact]
    public void AContractCanBeRescinded_WhileItsAccountStillOwesMoney()
    {
        var contract = ContractTestFactory.CreateActive(
            startDate: July, monthlyRent: 300_000m);

        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        account.Append(Rent(300_000m, July, JulyDue));

        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 7, 31));

        Assert.Equal(ContractStatus.Ended, contract.Status);
        Assert.Equal(300_000m, account.LedgerBalance(new DateOnly(2026, 7, 31)));
    }

    /// <summary>
    /// Spec test 27: payments land after the contract ended. The account outlives its contract, so
    /// a movement that reduces the balance is accepted and reduces it.
    ///
    /// Shown with a correction, because the payment movement itself belongs to the collection
    /// change — the specification puts it out of scope here and this capability only guarantees
    /// the account survives for it to be written against. What is proved is the guarantee: the
    /// account refuses nothing because its contract ended.
    /// </summary>
    [Fact]
    public void AnEndedContractsAccount_StillAcceptsAMovementThatReducesTheBalance()
    {
        var contract = ContractTestFactory.CreateActive(startDate: July, monthlyRent: 300_000m);
        var account = new ContractAccount(Guid.NewGuid(), contract.Id);
        var accrual = Rent(300_000m, July, JulyDue);
        account.Append(accrual);

        contract.End(EndReason.MutualAgreement, new DateOnly(2026, 7, 31));

        account.Append(AccountMovement.Correction(
            Guid.NewGuid(), -300_000m, new DateOnly(2026, 8, 15), accrual.Id, July));

        var asOf = new DateOnly(2026, 8, 31);

        Assert.Equal(0m, account.LedgerBalance(asOf));
        Assert.Equal(0m, account.AmountOwed(asOf, LeaseTerms));
    }

    [Fact]
    public void AmountOwed_RequiresTerms()
    {
        var account = NewAccount();

        Assert.Throws<ArgumentNullException>(() => account.AmountOwed(July, null!));
    }
}
