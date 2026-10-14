using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// Each payment issues exactly one locatario and one propietario recibo with consecutive numbers
/// (cobranza-y-recibos spec Tests 28, 29, 30). A pure function of the first number: the counter that
/// hands the number out is PR 5.
/// </summary>
public class ReciboPairTests
{
    private static readonly DateOnly Period = new(2026, 9, 1);
    private static readonly DateOnly ReceivedOn = new(2026, 9, 12);

    private static ReciboHeader Header() => new("Maria Gomez", ["Juan Perez"], "Departamento 2B");

    private static ReciboDraftPair Drafts() =>
        ReciboBuilder.Build(
            ReciboLedgerFigures.ForRentPeriod(507_000m, contractualFeeInstalment: null, recargo: 0m),
            TypedReciboLines.None,
            honorariosPercentage: 8m);

    // Test 28
    [Fact]
    public void APayment_YieldsOneLocatarioAndOnePropietarioRecibo()
    {
        var pair = ReciboPair.Issue(31_001, Period, ReceivedOn, Header(), Drafts());

        Assert.Equal(ReciboCopy.Locatario, pair.Locatario.Copy);
        Assert.Equal(ReciboCopy.Propietario, pair.Propietario.Copy);
        Assert.Equal(2, pair.Recibos.Count);
    }

    // Test 28
    [Fact]
    public void TheTwoRecibos_StateTheSamePeriodAndReceivedDate()
    {
        var pair = ReciboPair.Issue(31_001, Period, ReceivedOn, Header(), Drafts());

        Assert.Equal(Period, pair.Locatario.Period);
        Assert.Equal(Period, pair.Propietario.Period);
        Assert.Equal(ReceivedOn, pair.Locatario.ReceivedOn);
        Assert.Equal(ReceivedOn, pair.Propietario.ReceivedOn);
    }

    // Test 29
    [Fact]
    public void TheNumbersAreConsecutive_TheLocatarioTakingTheLower()
    {
        var pair = ReciboPair.Issue(31_001, Period, ReceivedOn, Header(), Drafts());

        Assert.Equal(31_001, pair.Locatario.Number);
        Assert.Equal(31_002, pair.Propietario.Number);
    }

    // Test 30
    [Fact]
    public void TwoPayments_AreFourRecibos_WithFourConsecutiveNumbers()
    {
        var first = ReciboPair.Issue(31_001, Period, ReceivedOn, Header(), Drafts());
        var second = ReciboPair.Issue(31_003, Period.AddMonths(1), ReceivedOn.AddMonths(1), Header(), Drafts());

        var numbers = first.Recibos.Concat(second.Recibos).Select(r => r.Number).ToArray();

        Assert.Equal([31_001L, 31_002L, 31_003L, 31_004L], numbers);
    }

    [Fact]
    public void TheNumbers_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReciboPair.Issue(0, Period, ReceivedOn, Header(), Drafts()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReciboPair.Issue(-5, Period, ReceivedOn, Header(), Drafts()));
    }

    [Fact]
    public void ThePeriod_MustBeTheFirstOfAMonth()
    {
        Assert.Throws<ArgumentException>(
            () => ReciboPair.Issue(31_001, new DateOnly(2026, 9, 10), ReceivedOn, Header(), Drafts()));
    }

    [Fact]
    public void TheRecibos_KeepTheDraftLinesAndTotals()
    {
        var drafts = Drafts();
        var pair = ReciboPair.Issue(31_001, Period, ReceivedOn, Header(), drafts);

        Assert.Equal(drafts.Locatario.Total, pair.Locatario.Total);
        Assert.Equal(drafts.Propietario.Total, pair.Propietario.Total);
        Assert.Equal(drafts.Propietario, pair.Propietario.AsDraft());
    }

    [Fact]
    public void ARecibo_IsImmutable_ItsLinesCannotBeChangedThroughTheList()
    {
        var recibo = ReciboPair.Issue(31_001, Period, ReceivedOn, Header(), Drafts()).Locatario;

        Assert.False(recibo.Lines is List<ReciboLine>);
        Assert.All(typeof(Recibo).GetProperties(), p => Assert.Null(p.SetMethod));
    }

    // R3-pair-number-overflow
    [Fact]
    public void AFirstNumberOfLongMaxValue_IsRefused_BecauseTheNextNumberCannotExist()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => ReciboPair.Issue(long.MaxValue, Period, ReceivedOn, Header(), Drafts()));

        Assert.Equal("firstNumber", ex.ParamName);
    }

    [Fact]
    public void TheLargestUsableFirstNumber_StillIssuesAPair()
    {
        var pair = ReciboPair.Issue(long.MaxValue - 1, Period, ReceivedOn, Header(), Drafts());

        Assert.Equal(long.MaxValue, pair.Propietario.Number);
    }

    [Fact]
    public void ADraftPairWithANullComponent_IsRefused()
    {
        var drafts = Drafts();

        Assert.Throws<ArgumentNullException>(() => new ReciboDraftPair(null!, drafts.Propietario));
        Assert.Throws<ArgumentNullException>(() => new ReciboDraftPair(drafts.Locatario, null!));
    }
}
