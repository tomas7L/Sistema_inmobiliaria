using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// The honorarios contractuales plan of a contract (cobranza-y-recibos spec Tests 60 to 63; design
/// section 8): an amount and an instalment count, both or neither, fixed once an instalment has been
/// written. Also the go-live cutover date setter (design D17), which has the same one-way shape.
/// </summary>
public class ContractualFeePlanTests
{
    private static readonly DateOnly July = new(2026, 7, 1);

    private static Contract NewContract() =>
        ContractTestFactory.CreateActive(startDate: new DateOnly(2026, 1, 1), monthlyRent: 500_000m);

    private static ContractAccount NewAccount(Contract contract) =>
        new(Guid.NewGuid(), contract.Id);

    /// <summary>Spec test 60: a plan of amount and count is stored.</summary>
    [Fact]
    public void APlanOfAmountAndCount_IsStored()
    {
        var contract = NewContract();
        var account = NewAccount(contract);

        contract.ChangeContractualFeePlan(new ContractualFeePlan(120_000m, 6), account);

        Assert.NotNull(contract.ContractualFeePlan);
        Assert.Equal(120_000m, contract.ContractualFeePlan!.Amount);
        Assert.Equal(6, contract.ContractualFeePlan.Instalments);
    }

    /// <summary>Spec test 60: no plan is valid, and it is the state every contract is born in.</summary>
    [Fact]
    public void NoPlanIsValid_AndIsTheDefault()
    {
        var contract = NewContract();
        var account = NewAccount(contract);

        Assert.Null(contract.ContractualFeePlan);

        contract.ChangeContractualFeePlan(new ContractualFeePlan(120_000m, 3), account);
        contract.ChangeContractualFeePlan(null, account);

        Assert.Null(contract.ContractualFeePlan);
    }

    [Fact]
    public void APlan_CanBeGivenAtConstruction()
    {
        var contract = new Contract(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(Guid.NewGuid(), 100m)],
            contractualFeePlan: new ContractualFeePlan(60_000m, 2));

        Assert.Equal(new ContractualFeePlan(60_000m, 2), contract.ContractualFeePlan);
    }

    /// <summary>Spec test 61: a count of 0 or 7 is refused; 1 and 6 are the edges that are accepted.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public void AnInstalmentCountOutsideOneToSix_IsRefused(int count)
    {
        Assert.Throws<ArgumentException>(() => new ContractualFeePlan(100_000m, count));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void AnInstalmentCountOfOneOrSix_IsAccepted(int count)
    {
        var plan = new ContractualFeePlan(100_000m, count);

        Assert.Equal(count, plan.Instalments);
    }

    /// <summary>Spec test 61: a non-positive amount is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50_000)]
    public void ANonPositiveAmount_IsRefused(int amount)
    {
        Assert.Throws<ArgumentException>(() => new ContractualFeePlan(amount, 3));
    }

    /// <summary>
    /// Spec test 61: an amount without a count, or the reverse, cannot even be expressed. The only
    /// public constructor takes both, so the both-or-neither rule holds by construction here and the
    /// database check constraint holds it against raw SQL.
    /// </summary>
    [Fact]
    public void AnAmountWithoutACount_CannotBeBuilt()
    {
        var constructors = typeof(ContractualFeePlan).GetConstructors();

        var only = Assert.Single(constructors);
        Assert.Equal([typeof(decimal), typeof(int)], only.GetParameters().Select(p => p.ParameterType));
    }

    /// <summary>
    /// Spec test 62: the plan amount is fixed. A rent adjustment moves the canon and leaves the plan
    /// exactly where it was.
    /// </summary>
    [Fact]
    public void ThePlanAmount_DoesNotMoveWhenTheRentAdjusts()
    {
        var contract = NewContract();
        contract.ChangeContractualFeePlan(new ContractualFeePlan(120_000m, 6), NewAccount(contract));

        ContractTestFactory.ConfirmAdjustment(
            contract, previousCanon: 500_000m, percentage: 20m, effectiveDate: new DateOnly(2026, 7, 1));

        Assert.Equal(600_000m, contract.MonthlyRent);
        Assert.Equal(new ContractualFeePlan(120_000m, 6), contract.ContractualFeePlan);
    }

    /// <summary>Spec test 63: once an instalment has been written the plan cannot change.</summary>
    [Fact]
    public void APlan_CannotBeChangedOnceAnInstalmentHasBeenWritten()
    {
        var contract = NewContract();
        var account = NewAccount(contract);
        var plan = new ContractualFeePlan(120_000m, 6);
        contract.ChangeContractualFeePlan(plan, account);
        account.Append(AccountMovement.ContractualFeeInstalment(
            Guid.NewGuid(), 20_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)));

        Assert.Throws<InvalidOperationException>(() =>
            contract.ChangeContractualFeePlan(new ContractualFeePlan(90_000m, 3), account));
        Assert.Throws<InvalidOperationException>(() =>
            contract.ChangeContractualFeePlan(null, account));

        Assert.Equal(plan, contract.ContractualFeePlan);
    }

    /// <summary>
    /// Review finding R3-fee-plan-after-rent-silently-dropped: the materialiser skips an already
    /// charged period as a whole, so a plan accepted after rent was written would never raise its
    /// instalments. The plan is therefore refused once the account holds ANY movement.
    /// </summary>
    [Fact]
    public void APlan_CannotBeSet_OnceTheAccountHoldsOnlyARentAccrual()
    {
        var contract = NewContract();
        var account = NewAccount(contract);
        account.Append(AccountMovement.RentAccrual(
            Guid.NewGuid(), 500_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)));

        Assert.Throws<InvalidOperationException>(() =>
            contract.ChangeContractualFeePlan(new ContractualFeePlan(80_000m, 4), account));

        Assert.Null(contract.ContractualFeePlan);
    }

    /// <summary>Same finding: an opening balance is a movement too, and locks the plan.</summary>
    [Fact]
    public void APlan_CannotBeSet_OnceTheAccountHoldsOnlyAnOpeningBalance()
    {
        var contract = NewContract();
        var account = NewAccount(contract);
        account.Append(AccountMovement.OpeningBalance(
            Guid.NewGuid(), 0m, new DateOnly(2026, 6, 30), period: null));

        Assert.Throws<InvalidOperationException>(() =>
            contract.ChangeContractualFeePlan(new ContractualFeePlan(80_000m, 4), account));

        Assert.Null(contract.ContractualFeePlan);
    }

    /// <summary>The plan stays freely changeable while the account is empty.</summary>
    [Fact]
    public void APlan_CanStillBeChanged_WhileTheAccountIsEmpty()
    {
        var contract = NewContract();
        var account = NewAccount(contract);

        contract.ChangeContractualFeePlan(new ContractualFeePlan(80_000m, 4), account);
        contract.ChangeContractualFeePlan(new ContractualFeePlan(60_000m, 2), account);

        Assert.Equal(new ContractualFeePlan(60_000m, 2), contract.ContractualFeePlan);
    }

    [Fact]
    public void APlan_CannotBeChangedThroughAnotherContractsAccount()
    {
        var contract = NewContract();
        var foreignAccount = NewAccount(NewContract());

        Assert.Throws<ArgumentException>(() =>
            contract.ChangeContractualFeePlan(new ContractualFeePlan(80_000m, 4), foreignAccount));
    }

    /// <summary>
    /// Design D17, unnumbered: the cutover date is set when a contract is loaded as predating the
    /// system, and then it floors the schedule.
    /// </summary>
    [Fact]
    public void TheCutoverDate_CanBeSet_OnAContractWhoseAccountHoldsNothing()
    {
        var contract = NewContract();
        var account = NewAccount(contract);
        var cutover = new DateOnly(2026, 6, 30);

        contract.SetCutoverDate(cutover, account, today: July);

        Assert.Equal(cutover, contract.CutoverDate);
        Assert.DoesNotContain(AccrualSchedule.PeriodsDue(contract, July.AddDays(20)), p => p.DueDate <= cutover);
    }

    /// <summary>Design D17, unnumbered: once the account holds any movement the cutover is one-way.</summary>
    [Fact]
    public void TheCutoverDate_CannotBeSet_OnceTheAccountHoldsAnyMovement()
    {
        var contract = NewContract();
        var account = NewAccount(contract);
        account.Append(AccountMovement.OpeningBalance(
            Guid.NewGuid(), 0m, new DateOnly(2026, 6, 30), period: null));

        Assert.Throws<InvalidOperationException>(() =>
            contract.SetCutoverDate(new DateOnly(2026, 6, 30), account, today: July));

        Assert.Null(contract.CutoverDate);
    }

    [Fact]
    public void TheCutoverDate_CannotBeSetThroughAnotherContractsAccount()
    {
        var contract = NewContract();
        var foreignAccount = NewAccount(NewContract());

        Assert.Throws<ArgumentException>(() =>
            contract.SetCutoverDate(new DateOnly(2026, 6, 30), foreignAccount, today: July));
    }
}
