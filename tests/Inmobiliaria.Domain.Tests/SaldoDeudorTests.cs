using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// A month loaded from the old spreadsheet as an OpeningBalance prints a Saldo Deudor line instead of
/// rent (cobranza-y-recibos spec Test 75; design D27, spec Decision 49). The honorarios base is that
/// month's figure, never the whole debt.
/// </summary>
public class SaldoDeudorTests
{
    private static ReciboDraftPair Build(decimal figure, decimal rate = 8m, TypedReciboLines? typed = null) =>
        ReciboBuilder.Build(ReciboLedgerFigures.ForOpeningBalance(figure), typed ?? TypedReciboLines.None, rate);

    private static ReciboLineKind[] KindsOf(ReciboDraft draft) => [.. draft.Lines.Select(l => l.Kind)];

    // Test 75
    [Fact]
    public void AnOpeningBalanceMonth_PrintsASaldoDeudorLine_OnBothRecibos_AndNoRentOrRecargo()
    {
        var drafts = Build(160_000m);

        Assert.Equal([ReciboLineKind.SaldoDeudor], KindsOf(drafts.Locatario));
        Assert.Equal(160_000m, drafts.Locatario.Lines.Single().Amount);

        Assert.Equal(
            [ReciboLineKind.SaldoDeudor, ReciboLineKind.HonorariosAdministracion],
            KindsOf(drafts.Propietario));
        Assert.Equal(160_000m, drafts.Propietario.Lines[0].Amount);
    }

    // Test 75
    [Fact]
    public void TheHonorariosLine_IsEightPercentOfTheFigure_MinusTwelveThousandEightHundred()
    {
        var drafts = Build(160_000m, rate: 8m);

        Assert.Equal(-12_800m, drafts.Propietario.Lines.Single(l => l.Kind == ReciboLineKind.HonorariosAdministracion).Amount);
        Assert.Equal(160_000m, drafts.Locatario.Total);
        Assert.Equal(147_200m, drafts.Propietario.Total);
    }

    [Fact]
    public void TheHonorariosBase_IsTheFigure_NotTheTypedLines()
    {
        var drafts = Build(160_000m, typed: new TypedReciboLines(TasaMunicipal: 5_000m));

        Assert.Equal(-12_800m, drafts.Propietario.Lines.Single(l => l.Kind == ReciboLineKind.HonorariosAdministracion).Amount);
        Assert.Contains(ReciboLineKind.TasaMunicipal, KindsOf(drafts.Locatario));
    }

    [Fact]
    public void AFractionalFeeOnTheFigure_IsTruncatedTowardZero()
    {
        // 8 / 100 x 160,011 = 12,800.88 -> -12,800.
        var drafts = Build(160_011m, rate: 8m);

        Assert.Equal(-12_800m, drafts.Propietario.Lines.Single(l => l.Kind == ReciboLineKind.HonorariosAdministracion).Amount);
    }

    [Fact]
    public void ASaldoDeudorFigure_MayNotBeNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReciboLedgerFigures.ForOpeningBalance(-1m));
    }

    [Fact]
    public void TheCatalog_TreatsSaldoDeudorAsLedgerDerived_OnBothCopies()
    {
        Assert.True(ReciboLineCatalog.IsLedgerDerived(ReciboLineKind.SaldoDeudor));
        Assert.True(ReciboLineCatalog.AppearsOn(ReciboLineKind.SaldoDeudor, ReciboCopy.Locatario));
        Assert.True(ReciboLineCatalog.AppearsOn(ReciboLineKind.SaldoDeudor, ReciboCopy.Propietario));
    }
}
