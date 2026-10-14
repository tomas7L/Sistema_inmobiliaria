using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// A pre-existing debt is loaded month by month (cobranza-y-recibos spec Tests 52 to 56). These
/// replace the living contract-account Tests 9 and 10, which allowed exactly one period-less
/// opening balance and so made the debt impossible to settle one month at a time.
/// </summary>
public class OpeningBalanceTests
{
    private static readonly DateOnly May = new(2026, 5, 1);
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly July = new(2026, 7, 1);

    /// <summary>The day the figures were typed. An opening balance is dated on the day it is entered.</summary>
    private static readonly DateOnly TypedOn = new(2026, 10, 5);

    private static readonly RecargoTerms LeaseTerms = new(0.02m, graceDays: 0);

    private static ContractAccount NewAccount() => new(Guid.NewGuid(), Guid.NewGuid());

    private static AccountMovement Opening(decimal amount, DateOnly? period) =>
        AccountMovement.OpeningBalance(Guid.NewGuid(), amount, TypedOn, period);

    private static AccountMovement Rent(decimal amount, DateOnly period) =>
        AccountMovement.RentAccrual(Guid.NewGuid(), amount, period, period.AddDays(9));

    /// <summary>Spec test 52: three monthly opening balances sum to the ledger balance.</summary>
    [Fact]
    public void ThreeMonthlyOpeningBalances_SumToTheLedgerBalance()
    {
        var account = NewAccount();

        account.Append(Opening(150_000m, May));
        account.Append(Opening(160_000m, June));
        account.Append(Opening(170_000m, July));

        Assert.Equal(3, account.Movements.Count(m => m.Kind == MovementKind.OpeningBalance));
        Assert.Equal(480_000m, account.LedgerBalance(TypedOn));
        Assert.Equal(
            [May, June, July],
            account.Movements.Select(m => m.Period!.Value));
    }

    /// <summary>Spec test 54, first part: a non-zero figure with no month cannot be settled by any payment.</summary>
    [Fact]
    public void ANonZeroOpeningBalanceWithNoPeriod_IsRefused()
    {
        var account = NewAccount();

        Assert.Throws<InvalidOperationException>(() => account.Append(Opening(150_000m, period: null)));
        Assert.Empty(account.Movements);
    }

    /// <summary>
    /// Spec test 54, second part: zero with no period is how a tenant who is up to date is loaded,
    /// so the account says somebody asserted zero rather than that nobody looked.
    /// </summary>
    [Fact]
    public void AZeroOpeningBalanceWithNoPeriod_IsAccepted()
    {
        var account = NewAccount();

        account.Append(Opening(0m, period: null));

        Assert.Single(account.Movements);
        Assert.Equal(0m, account.LedgerBalance(TypedOn));
    }

    /// <summary>Spec test 54, third part: a month is asserted once.</summary>
    [Fact]
    public void ASecondOpeningBalanceForOnePeriod_IsRefused()
    {
        var account = NewAccount();
        account.Append(Opening(150_000m, June));

        Assert.Throws<InvalidOperationException>(() => account.Append(Opening(999m, June)));
        Assert.Single(account.Movements);
    }

    /// <summary>Spec test 54, fourth part: a period holding an opening balance takes no rent accrual.</summary>
    [Fact]
    public void APeriodHoldingAnOpeningBalance_TakesNoRentAccrual()
    {
        var account = NewAccount();
        account.Append(Opening(150_000m, July));

        Assert.Throws<InvalidOperationException>(() => account.Append(Rent(500_000m, July)));
        Assert.Single(account.Movements);
    }

    /// <summary>The same single-charge rule read the other way: the period is already accrued.</summary>
    [Fact]
    public void APeriodAlreadyAccrued_TakesNoOpeningBalance()
    {
        var account = NewAccount();
        account.Append(Rent(500_000m, July));

        Assert.Throws<InvalidOperationException>(() => account.Append(Opening(150_000m, July)));
        Assert.Single(account.Movements);
    }

    /// <summary>Design D18: at most one period-less zero.</summary>
    [Fact]
    public void ASecondPeriodlessZero_IsRefused()
    {
        var account = NewAccount();
        account.Append(Opening(0m, period: null));

        Assert.Throws<InvalidOperationException>(() => account.Append(Opening(0m, period: null)));
    }

    /// <summary>
    /// Design D18, the weak rule: an account that carries monthly figures does not also say
    /// "nothing owed". It only prevents a confusing mix.
    /// </summary>
    [Fact]
    public void APeriodlessZero_IsRefusedOnAnAccountHoldingPeriodOpeningBalances()
    {
        var account = NewAccount();
        account.Append(Opening(150_000m, June));

        Assert.Throws<InvalidOperationException>(() => account.Append(Opening(0m, period: null)));
    }

    /// <summary>
    /// Review finding R3-weak-rule-one-directional: the mix rule is symmetric. A monthly figure is
    /// refused on an account that already asserted a period-less zero.
    /// </summary>
    [Fact]
    public void AMonthlyOpeningBalance_IsRefusedOnAnAccountHoldingAPeriodlessZero()
    {
        var account = NewAccount();
        account.Append(Opening(0m, period: null));

        Assert.Throws<InvalidOperationException>(() => account.Append(Opening(150_000m, June)));
        Assert.Single(account.Movements);
    }

    /// <summary>
    /// Review finding R3-period-not-normalised: a period is the first day of its month, or
    /// <c>IsChargedFor</c> would compare exact dates and let one month be charged twice.
    /// </summary>
    [Fact]
    public void EveryPeriodCarryingFactory_RefusesAPeriodThatIsNotTheFirstOfAMonth()
    {
        var midMonth = new DateOnly(2026, 6, 15);
        var dueOn = new DateOnly(2026, 6, 25);

        Assert.Throws<ArgumentException>(() => Opening(150_000m, midMonth));
        Assert.Throws<ArgumentException>(() =>
            AccountMovement.RentAccrual(Guid.NewGuid(), 500_000m, midMonth, dueOn));
        Assert.Throws<ArgumentException>(() =>
            AccountMovement.ContractualFeeInstalment(Guid.NewGuid(), 10_000m, midMonth, dueOn));
        Assert.Throws<ArgumentException>(() =>
            AccountMovement.RecargoCharged(Guid.NewGuid(), 1_000m, midMonth, dueOn));
        Assert.Throws<ArgumentException>(() =>
            AccountMovement.Payment(Guid.NewGuid(), -1_000m, midMonth, dueOn));
    }

    /// <summary>Spec test 56: an opening balance stays exactly as it was typed after later movements.</summary>
    [Fact]
    public void AnOpeningBalance_IsUnchangedByLaterMovements()
    {
        var account = NewAccount();
        var june = Opening(160_000m, June);
        account.Append(june);

        account.Append(Rent(500_000m, July));
        account.Append(AccountMovement.Payment(
            Guid.NewGuid(), -160_000m, June, new DateOnly(2026, 10, 20)));

        var stored = Assert.Single(account.Movements, m => m.Kind == MovementKind.OpeningBalance);
        Assert.Same(june, stored);
        Assert.Equal(160_000m, stored.Amount);
        Assert.Equal(June, stored.Period);
        Assert.Equal(TypedOn, stored.OccurredOn);
    }

    /// <summary>
    /// Spec test 55: a month loaded as an opening balance projects no recargo, however overdue.
    /// The typed figure is what the agency's spreadsheet already says is owed, mora included
    /// (spec Decision 31). Paying it therefore writes no RecargoCharged: the planner-level repeat
    /// is asserted again, unnumbered, in task 4.4.
    /// </summary>
    [Fact]
    public void AnOpeningBalanceMonth_ProjectsNoRecargo()
    {
        var account = NewAccount();
        account.Append(Opening(160_000m, June));

        var wellAfterwards = new DateOnly(2027, 6, 30);

        Assert.Equal(0m, account.RecargoProjectedFor(June, wellAfterwards, LeaseTerms));
        Assert.Equal(160_000m, account.AmountOwed(wellAfterwards, LeaseTerms));
    }
}
