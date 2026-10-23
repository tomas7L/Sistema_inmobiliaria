using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.CashDesk;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza design section 7.2: what a stale refusal shows the operator, matched by copy and kind.</summary>
public class PreviewDifferenceTests
{
    private static CollectionPreview Preview(
        DateOnly? period, DateOnly received, decimal previous, decimal ledger, decimal resulting,
        ReciboLine[] locatario, ReciboLine[] propietario) =>
        new(period, received, received, false, previous, ledger, resulting,
            new ReciboDraft(ReciboCopy.Locatario, locatario), new ReciboDraft(ReciboCopy.Propietario, propietario), [], null);

    private static ReciboLine Line(ReciboLineKind kind, int position, decimal amount) => new(kind, position, amount);

    [Fact]
    public void TheSamePreview_HasNoDifference()
    {
        var preview = Preview(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 100m, 100m, 0m,
            [Line(ReciboLineKind.Rent, 1, 100m)], [Line(ReciboLineKind.Rent, 1, 100m)]);

        Assert.Empty(PreviewDifference.Between(preview, preview));
    }

    [Fact]
    public void EveryFigure_AndEveryLineChangedAppearedOrDisappeared_IsListed()
    {
        var before = Preview(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 598_260m, 598_260m, 0m,
            [Line(ReciboLineKind.Rent, 1, 500_000m), Line(ReciboLineKind.Recargo, 2, 98_260m)],
            [Line(ReciboLineKind.Rent, 1, 500_000m), Line(ReciboLineKind.HonorariosAdministracion, 2, -47_860m)]);
        var after = Preview(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 16), 605_000m, 605_000m, 0m,
            [Line(ReciboLineKind.Rent, 1, 500_000m), Line(ReciboLineKind.Recargo, 2, 105_000m), Line(ReciboLineKind.Seguro, 3, 4_000m)],
            [Line(ReciboLineKind.Rent, 1, 500_000m)]);

        Assert.Equal(
            [
                new DifferenceLine("Período", "01/2026", "02/2026"),
                new DifferenceLine("Fecha de cobro", "15/01/2026", "16/01/2026"),
                new DifferenceLine("Saldo anterior", "598.260", "605.000"),
                new DifferenceLine("Saldo del período", "598.260", "605.000"),
                new DifferenceLine("Recibo locatario · Recargo por mora", "98.260", "105.000"),
                new DifferenceLine("Recibo locatario · Seguro", "—", "4.000"),
                new DifferenceLine("Recibo propietario · Honorarios de administración", "-47.860", "—"),
            ],
            PreviewDifference.Between(before, after));
    }

    [Fact]
    public void AMissingDraftOrPeriod_ReadsAsADash()
    {
        var before = new CollectionPreview(null, new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15), false,
            0m, 0m, 0m, null, null, [], CollectionRefusal.NothingPayable);
        var after = Preview(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 0m, 0m, 0m,
            [Line(ReciboLineKind.Rent, 1, 100m)], []);

        Assert.Equal(
            [
                new DifferenceLine("Período", "—", "01/2026"),
                new DifferenceLine("Recibo locatario · Alquiler", "—", "100"),
            ],
            PreviewDifference.Between(before, after));
    }
}
