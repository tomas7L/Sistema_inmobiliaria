using Inmobiliaria.Presentation.Printing;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec test 24d, Decision 19, design D11: the paper rule. The real output is M1.</summary>
public class SheetArrangementTests
{
    [Theory]
    [InlineData(297, 210, 1, SheetLayout.TwoUp)]
    [InlineData(279.4, 215.9, 1, SheetLayout.TwoUp)]
    [InlineData(210, 297, 1, SheetLayout.OnePerPage)]
    [InlineData(210, 148, 1, SheetLayout.OnePerPage)]
    [InlineData(239, 160, 1, SheetLayout.OnePerPage)]
    [InlineData(240, 160, 1, SheetLayout.TwoUp)]
    [InlineData(297, 210, 2, SheetLayout.OnePerPage)]
    public void TheLayout_FollowsThePrintableArea(double widthMm, double heightMm, int halfPages, SheetLayout layout) =>
        Assert.Equal(layout, SheetArrangement.For(widthMm, heightMm, halfPages));

    [Fact]
    public void TheMinimumHalfWidth_IsTheDesignsProvisionalValue() =>
        Assert.Equal(120, SheetArrangement.MinimumHalfWidthMm);

    [Fact]
    public void DevicePixels_ConvertToMillimetres() =>
        Assert.Equal(297, SheetArrangement.Millimetres(297 / 25.4 * 96), precision: 6);
}
