using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// ui-de-cobranza spec tests 32 and 34 (domain half), design section 6.1: a cutover date lies between the
/// contract's start and the agency's today, both included, and cannot change once the account holds a
/// movement. <see cref="Contract.CheckCutoverDate"/> answers as a value; <see cref="Contract.SetCutoverDate"/>
/// still throws, so a caller that skips the check cannot store a bad date.
/// </summary>
public class CutoverDateRuleTests
{
    private static readonly DateOnly Start = new(2025, 3, 1);
    private static readonly DateOnly Today = new(2026, 10, 20);

    private static Contract NewContract(DateOnly? start = null) =>
        ContractTestFactory.CreateActive(startDate: start ?? Start, monthlyRent: 500_000m);

    private static ContractAccount NewAccount(Contract contract) =>
        new(Guid.NewGuid(), contract.Id);

    private static ContractAccount AccountWithMovement(Contract contract)
    {
        var account = NewAccount(contract);
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 0m, Start, period: null));
        return account;
    }

    /// <summary>Spec test 32: the day after today is refused, and nothing is stored.</summary>
    [Fact]
    public void ADateAfterToday_IsRefused_AndTheStoredDateIsUnchanged()
    {
        var contract = NewContract();
        var account = NewAccount(contract);

        Assert.Equal(CutoverRefusal.AfterToday, contract.CheckCutoverDate(Today.AddDays(1), account, Today));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            contract.SetCutoverDate(Today.AddDays(1), account, Today));

        Assert.Null(contract.CutoverDate);
    }

    /// <summary>Spec test 32: the day before the contract's start is refused, and a stored date stays.</summary>
    [Fact]
    public void ADateBeforeTheContractStart_IsRefused_AndTheStoredDateIsUnchanged()
    {
        var contract = NewContract();
        var account = NewAccount(contract);
        var stored = new DateOnly(2026, 10, 5);
        contract.SetCutoverDate(stored, account, Today);

        Assert.Equal(
            CutoverRefusal.BeforeContractStart,
            contract.CheckCutoverDate(new DateOnly(2025, 2, 28), account, Today));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            contract.SetCutoverDate(new DateOnly(2025, 2, 28), account, Today));

        Assert.Equal(stored, contract.CutoverDate);
    }

    /// <summary>Spec test 32: today and the start date themselves are accepted.</summary>
    [Fact]
    public void TodayAndTheStartDate_AreAccepted()
    {
        var contract = NewContract();
        var account = NewAccount(contract);

        Assert.Null(contract.CheckCutoverDate(Today, account, Today));
        contract.SetCutoverDate(Today, account, Today);
        Assert.Equal(Today, contract.CutoverDate);

        Assert.Null(contract.CheckCutoverDate(Start, account, Today));
        contract.SetCutoverDate(Start, account, Today);
        Assert.Equal(Start, contract.CutoverDate);
    }

    /// <summary>Spec test 34, domain half: a movement still blocks, even for a date inside the bounds.</summary>
    [Fact]
    public void AMovement_StillBlocks()
    {
        var contract = NewContract();
        var account = AccountWithMovement(contract);

        Assert.Equal(CutoverRefusal.AccountHasMovement, contract.CheckCutoverDate(Today, account, Today));
        Assert.Throws<InvalidOperationException>(() => contract.SetCutoverDate(Today, account, Today));

        Assert.Null(contract.CutoverDate);
    }

    /// <summary>Design section 6.1, order of checks: own account comes first, and is still an exception.</summary>
    [Fact]
    public void AnotherContractsAccount_IsCheckedFirst()
    {
        var contract = NewContract();
        var foreignAccount = AccountWithMovement(NewContract());

        Assert.Throws<ArgumentException>(() =>
            contract.CheckCutoverDate(Today.AddDays(1), foreignAccount, Today));
        Assert.Throws<ArgumentException>(() =>
            contract.SetCutoverDate(Today.AddDays(1), foreignAccount, Today));
    }

    /// <summary>Design section 6.1, order of checks: a movement is named before a date out of bounds.</summary>
    [Fact]
    public void AMovement_IsNamedBeforeTheBounds()
    {
        var contract = NewContract();
        var account = AccountWithMovement(contract);

        Assert.Equal(
            CutoverRefusal.AccountHasMovement,
            contract.CheckCutoverDate(Today.AddDays(1), account, Today));
        Assert.Throws<InvalidOperationException>(() =>
            contract.SetCutoverDate(Today.AddDays(1), account, Today));
    }

    /// <summary>
    /// Design section 6.1, order of checks: after today is named before before-start. Only a contract that
    /// starts in the future can break both at once.
    /// </summary>
    [Fact]
    public void AfterToday_IsNamedBeforeBeforeStart()
    {
        var contract = NewContract(start: new DateOnly(2026, 12, 1));
        var account = NewAccount(contract);

        Assert.Equal(
            CutoverRefusal.AfterToday,
            contract.CheckCutoverDate(new DateOnly(2026, 11, 1), account, Today));
    }
}
