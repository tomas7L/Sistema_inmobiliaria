using Inmobiliaria.Domain.Accounts;

namespace Inmobiliaria.Domain.Tests;

public class AccountTests
{
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly August = new(2026, 8, 1);
    private static readonly DateOnly September = new(2026, 9, 1);

    private static ContractAccount AccountFor(Guid contractId) => new(Guid.NewGuid(), contractId);

    /// <summary>Rent for a period, falling due on that month's 10th — the agency's default due day.</summary>
    private static AccountMovement Rent(decimal amount, DateOnly period) =>
        AccountMovement.RentAccrual(Guid.NewGuid(), amount, period, period.AddDays(9));

    /// <summary>Spec test 1: an account belongs to exactly one contract.</summary>
    [Fact]
    public void Account_BelongsToOneContract_AndCannotBeOrphaned()
    {
        var contractId = Guid.NewGuid();

        var account = AccountFor(contractId);

        Assert.Equal(contractId, account.ContractId);
        Assert.Throws<ArgumentException>(() => new ContractAccount(Guid.NewGuid(), Guid.Empty));
    }

    /// <summary>
    /// Spec test 2, domain half: a tenant holding two contracts has two accounts, and what that
    /// tenant owes is summed over them rather than held anywhere.
    /// </summary>
    [Fact]
    public void TwoContractsOfOneTenant_AreTwoAccounts_SummedOnDemand()
    {
        var apartment = AccountFor(Guid.NewGuid());
        var garage = AccountFor(Guid.NewGuid());
        apartment.Append(Rent(500_000m, July));
        garage.Append(Rent(80_000m, July));

        Assert.NotEqual(apartment.ContractId, garage.ContractId);

        var asOf = new DateOnly(2026, 7, 31);
        var tenantTotal = apartment.LedgerBalance(asOf) + garage.LedgerBalance(asOf);

        Assert.Equal(580_000m, tenantTotal);
    }

    /// <summary>Spec test 6, first half: the ledger balance is the sum of the movements.</summary>
    [Fact]
    public void LedgerBalance_IsTheSumOfMovements()
    {
        var account = AccountFor(Guid.NewGuid());
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 150_000m, July));
        account.Append(Rent(500_000m, July));

        Assert.Equal(650_000m, account.LedgerBalance(new DateOnly(2026, 7, 31)));
    }

    /// <summary>Spec test 7: a historical balance counts only movements dated on or before it.</summary>
    [Fact]
    public void LedgerBalance_CountsOnlyMovementsUpToTheDateAsked()
    {
        var account = AccountFor(Guid.NewGuid());
        account.Append(Rent(500_000m, July));
        account.Append(Rent(500_000m, August));
        account.Append(Rent(500_000m, September));

        // Each accrual falls due on its own 10th, so a question asked mid-August has seen two.
        Assert.Equal(1_000_000m, account.LedgerBalance(new DateOnly(2026, 8, 15)));
        Assert.Equal(1_500_000m, account.LedgerBalance(new DateOnly(2026, 9, 30)));
    }

    /// <summary>
    /// Spec test 8: a contract created in the system opens with an empty account. This is the
    /// ordinary case, a tenant who signs tomorrow, and an earlier draft of the specification would
    /// have refused it.
    /// </summary>
    [Fact]
    public void AnAccountWithNoMovements_IsValidAndReadsZero()
    {
        var account = AccountFor(Guid.NewGuid());

        Assert.Empty(account.Movements);
        Assert.Equal(0m, account.LedgerBalance(new DateOnly(2026, 7, 31)));
    }

    /// <summary>Spec test 9: a pre-existing contract carries one opening balance; a second is refused.</summary>
    [Fact]
    public void OpeningBalance_IsAssertedOnce_AndNeverRestated()
    {
        var account = AccountFor(Guid.NewGuid());
        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 150_000m, July));

        var second = AccountMovement.OpeningBalance(Guid.NewGuid(), 999m, August);

        Assert.Throws<InvalidOperationException>(() => account.Append(second));
    }

    /// <summary>
    /// Spec test 9, second half: zero is valid for a tenant who is up to date, and the movement is
    /// still recorded. The account must say somebody asserted zero, not that nobody looked.
    /// </summary>
    [Fact]
    public void ZeroIsAValidOpeningBalance_AndIsStillRecorded()
    {
        var account = AccountFor(Guid.NewGuid());

        account.Append(AccountMovement.OpeningBalance(Guid.NewGuid(), 0m, July));

        Assert.Single(account.Movements);
        Assert.Equal(0m, account.LedgerBalance(July));
    }

    /// <summary>Spec test 10: an opening balance never changes once later movements arrive.</summary>
    [Fact]
    public void OpeningBalance_IsUnchangedByLaterMovements()
    {
        var account = AccountFor(Guid.NewGuid());
        var opening = AccountMovement.OpeningBalance(Guid.NewGuid(), 150_000m, July);
        account.Append(opening);

        account.Append(Rent(500_000m, August));

        var stored = Assert.Single(account.Movements, m => m.Kind == MovementKind.OpeningBalance);
        Assert.Equal(150_000m, stored.Amount);
        Assert.Equal(opening.Id, stored.Id);
    }

    /// <summary>Spec test 5: a correction appends and names what it corrects; the original stands.</summary>
    [Fact]
    public void ACorrection_AppendsTheDifference_AndLeavesTheOriginalIntact()
    {
        var account = AccountFor(Guid.NewGuid());
        var wrong = Rent(500_000m, July);
        account.Append(wrong);

        // The accrual should have been 450,000. Append the difference rather than editing.
        account.Append(AccountMovement.Correction(
            Guid.NewGuid(), -50_000m, new DateOnly(2026, 7, 20), wrong.Id, July));

        var original = Assert.Single(account.Movements, m => m.Id == wrong.Id);
        Assert.Equal(500_000m, original.Amount);
        Assert.Equal(450_000m, account.LedgerBalance(new DateOnly(2026, 7, 31)));
    }

    [Fact]
    public void ACorrection_MustNameAMovementOfThisAccount()
    {
        var account = AccountFor(Guid.NewGuid());
        account.Append(Rent(500_000m, July));

        var pointingElsewhere = AccountMovement.Correction(
            Guid.NewGuid(), -1m, July, Guid.NewGuid(), July);

        Assert.Throws<InvalidOperationException>(() => account.Append(pointingElsewhere));
    }

    /// <summary>Spec test 14: with July, August and September unpaid, July is the one settled.</summary>
    [Fact]
    public void OldestUnpaidPeriod_IsTheEarliestOneStillOwing()
    {
        var account = AccountFor(Guid.NewGuid());
        account.Append(Rent(500_000m, September));
        account.Append(Rent(500_000m, July));
        account.Append(Rent(500_000m, August));

        Assert.Equal(July, account.OldestUnpaidPeriod());
    }

    /// <summary>
    /// Spec test 15: once a period nets to zero it stops being the oldest unpaid one, and the
    /// others are untouched. This is what a payment will do when collection adds it. The rule is
    /// netting rather than the presence of a payment row, so nothing here changes when that kind
    /// arrives.
    /// </summary>
    [Fact]
    public void ASettledPeriod_DropsOut_AndTheOthersRemain()
    {
        var account = AccountFor(Guid.NewGuid());
        var july = Rent(500_000m, July);
        account.Append(july);
        account.Append(Rent(500_000m, August));

        account.Append(AccountMovement.Correction(
            Guid.NewGuid(), -500_000m, new DateOnly(2026, 7, 25), july.Id, July));

        Assert.Equal(August, account.OldestUnpaidPeriod());
    }

    [Fact]
    public void AClearAccount_HasNoOldestUnpaidPeriod()
    {
        var account = AccountFor(Guid.NewGuid());

        Assert.Null(account.OldestUnpaidPeriod());
    }

    [Fact]
    public void HasAccrualFor_AnswersWhetherAPeriodIsAlreadyMaterialised()
    {
        var account = AccountFor(Guid.NewGuid());
        account.Append(Rent(500_000m, July));

        Assert.True(account.HasAccrualFor(July));
        Assert.False(account.HasAccrualFor(August));
    }

    /// <summary>Spec test 13: a period charging nothing is a missing accrual, not a period.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARentAccrual_MustBePositive(decimal amount)
    {
        Assert.Throws<ArgumentException>(
            () => AccountMovement.RentAccrual(Guid.NewGuid(), amount, July, July));
    }

    [Fact]
    public void ACorrectionOfZero_IsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => AccountMovement.Correction(Guid.NewGuid(), 0m, July, Guid.NewGuid()));
    }

    [Fact]
    public void ACorrection_MustNameTheMovementItCorrects()
    {
        Assert.Throws<ArgumentException>(
            () => AccountMovement.Correction(Guid.NewGuid(), -1m, July, Guid.Empty));
    }

    /// <summary>
    /// Spec test 27: money is decimal and never floating point. Checked by reflection over the
    /// real types rather than by searching the source, because a grep is satisfied by a comment
    /// and misses a float arriving through a constructor parameter.
    /// </summary>
    [Fact]
    public void NothingInAccounts_UsesFloatingPoint()
    {
        var accountsTypes = typeof(ContractAccount).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Inmobiliaria.Domain.Accounts")
            .ToList();

        Assert.NotEmpty(accountsTypes);

        var offenders = new List<string>();

        foreach (var type in accountsTypes)
        {
            foreach (var property in type.GetProperties())
            {
                if (IsFloatingPoint(property.PropertyType))
                {
                    offenders.Add($"{type.Name}.{property.Name}");
                }
            }

            foreach (var field in type.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic))
            {
                if (IsFloatingPoint(field.FieldType))
                {
                    offenders.Add($"{type.Name}.{field.Name}");
                }
            }

            foreach (var method in type.GetMethods())
            {
                if (IsFloatingPoint(method.ReturnType))
                {
                    offenders.Add($"{type.Name}.{method.Name}() returns");
                }

                foreach (var parameter in method.GetParameters())
                {
                    if (IsFloatingPoint(parameter.ParameterType))
                    {
                        offenders.Add($"{type.Name}.{method.Name}({parameter.Name})");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static bool IsFloatingPoint(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;
        return bare == typeof(double) || bare == typeof(float);
    }
}
