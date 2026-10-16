using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// The honorarios de administracion line of the propietario recibo (cobranza-y-recibos spec Tests 42,
/// 43, 44, 46; design D25, spec Decision 46). Recibo N 24270 is the worked example.
/// </summary>
public class HonorariosLineTests
{
    private static readonly DateOnly Period = new(2026, 9, 1);
    private static readonly DateOnly ReceivedOn = new(2026, 9, 14);

    private static ReciboDraftPair Build(decimal rent, decimal recargo, decimal rate) =>
        ReciboBuilder.Build(
            ReciboLedgerFigures.ForRentPeriod(rent, contractualFeeInstalment: null, recargo),
            TypedReciboLines.None,
            rate);

    private static decimal? Honorarios(ReciboDraft draft) =>
        draft.Lines.SingleOrDefault(l => l.Kind == ReciboLineKind.HonorariosAdministracion)?.Amount;

    private static Contract ContractWithRate(decimal? rate) =>
        new(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 1),
            new DateOnly(2027, 12, 31),
            monthlyRent: 500_000m,
            unitShares: [new UnitShare(Guid.NewGuid(), 100m)],
            honorariosPercentage: rate);

    // Test 42
    [Fact]
    public void TheWorkedExample_Recibo24270_Is_MinusFortySevenThousandEightHundredSixty()
    {
        // 8 / 100 x (507,000 + 91,260) = 47,860.80, truncated toward zero, then negated.
        var drafts = Build(rent: 507_000m, recargo: 91_260m, rate: 8m);

        Assert.Equal(-47_860m, Honorarios(drafts.Propietario));
    }

    // Test 42
    [Fact]
    public void TheLocatarioRecibo_HasNoHonorariosLine()
    {
        var drafts = Build(rent: 507_000m, recargo: 91_260m, rate: 8m);

        Assert.Null(Honorarios(drafts.Locatario));
    }

    // Test 42
    [Fact]
    public void TheHonorariosBase_IsRentPlusRecargo_NotTheInstalmentNorTheTypedLines()
    {
        var drafts = ReciboBuilder.Build(
            ReciboLedgerFigures.ForRentPeriod(500_000m, contractualFeeInstalment: 90_000m, recargo: 0m),
            new TypedReciboLines(TasaMunicipal: 7_000m, Seguro: 3_000m, OtrosConceptos: 11_000m),
            honorariosPercentage: 10m);

        Assert.Equal(-50_000m, Honorarios(drafts.Propietario));
    }

    [Fact]
    public void TheHonorariosLine_IsTruncatedTowardZeroBeforeItIsNegated()
    {
        // 10.5 / 100 x 1,999 = 209.895 -> 209, never 210.
        Assert.Equal(-209m, Honorarios(Build(rent: 1_999m, recargo: 0m, rate: 10.5m).Propietario));
    }

    [Fact]
    public void AZeroRate_PrintsAHonorariosLineOfZero()
    {
        var line = Build(rent: 507_000m, recargo: 0m, rate: 0m).Propietario.Lines
            .Single(l => l.Kind == ReciboLineKind.HonorariosAdministracion);

        Assert.Equal(0m, line.Amount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void ARateOutsideZeroToOneHundred_IsRefused(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Build(rent: 507_000m, recargo: 0m, rate: (decimal)rate));
    }

    [Fact]
    public void TheHonorariosLine_IsTheLastLineOfThePropietarioRecibo()
    {
        var propietario = Build(rent: 507_000m, recargo: 91_260m, rate: 8m).Propietario;

        Assert.Equal(ReciboLineKind.HonorariosAdministracion, propietario.Lines[^1].Kind);
    }

    // Test 43
    [Fact]
    public void AnIssuedRecibo_IsNeverRecomputedAgainstAnotherRate()
    {
        var recibo = Issue(Build(rent: 507_000m, recargo: 91_260m, rate: 8m)).Propietario;
        var totalAsIssued = recibo.Total;

        // A contract carrying a different rate exists beside it. The recibo does not read it.
        var contract = ContractWithRate(12m);
        Assert.Equal(12m, contract.HonorariosPercentage);

        Assert.Equal(-47_860m, Honorarios(recibo.AsDraft()));
        Assert.Equal(totalAsIssued, recibo.Total);
    }

    // Test 43
    [Fact]
    public void ARecibo_ExposesNoMemberThatReadsAContractOrARate()
    {
        var offenders = typeof(Recibo).GetMethods()
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(Contract)))
            .Select(m => m.Name);

        Assert.Empty(offenders);
        Assert.DoesNotContain(
            typeof(Recibo).GetProperties(),
            p => p.Name.Contains("Percentage", StringComparison.Ordinal) || p.Name.Contains("Rate", StringComparison.Ordinal));
    }

    // Test 44
    [Fact]
    public void SeveralLessors_AppearOnOnePropietarioRecibo()
    {
        var header = new ReciboHeader("Maria Gomez", ["Juan Perez", "Ana Ruiz", "Luis Soto"], "Departamento 2B");

        var recibo = ReciboPair.Issue(
            24_270, Period, ReceivedOn, header, Build(rent: 507_000m, recargo: 91_260m, rate: 8m)).Propietario;

        Assert.Equal(["Juan Perez", "Ana Ruiz", "Luis Soto"], recibo.Header.Lessors);
    }

    [Fact]
    public void AHeader_IsASnapshot_ALaterChangeToTheSourceListDoesNotReachIt()
    {
        var lessors = new List<string> { "Juan Perez" };
        var header = new ReciboHeader("Maria Gomez", lessors, "Departamento 2B");

        lessors.Add("Someone Else");

        Assert.Equal(["Juan Perez"], header.Lessors);
    }

    // Test 46
    [Fact]
    public void NoHonorariosMovement_IsEverWrittenToTheAccount()
    {
        Assert.DoesNotContain(
            Enum.GetNames<MovementKind>(),
            name => name.Contains("Honorarios", StringComparison.OrdinalIgnoreCase));

        var account = new ContractAccount(Guid.NewGuid(), Guid.NewGuid());
        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 507_000m, Period, new DateOnly(2026, 9, 10)));
        var movementsBefore = account.Movements.Count;
        var balanceBefore = account.LedgerBalance(ReceivedOn);

        _ = Build(rent: 507_000m, recargo: 91_260m, rate: 8m);

        Assert.Equal(movementsBefore, account.Movements.Count);
        Assert.Equal(balanceBefore, account.LedgerBalance(ReceivedOn));
    }

    private static ReciboPair Issue(ReciboDraftPair drafts) =>
        ReciboPair.Issue(
            24_270, Period, ReceivedOn, new ReciboHeader("Maria Gomez", ["Juan Perez"], "Departamento 2B"), drafts);

    // R3-001: a zero fee is 0m, never negative zero.
    [Theory]
    [InlineData(0, 507_000, 91_260)]
    [InlineData(8, 0, 0)]
    [InlineData(8, 5, 0)]
    public void AZeroFee_IsPrintedAsPlainZero_NeverNegativeZero(int rate, int rent, int recargo)
    {
        var fee = Honorarios(Build(rent, recargo, rate).Propietario);

        Assert.Equal(0m, fee);
        Assert.False(decimal.IsNegative(fee!.Value));
        Assert.Equal("0", fee.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
