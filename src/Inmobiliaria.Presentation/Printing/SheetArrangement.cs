namespace Inmobiliaria.Presentation.Printing;

/// <summary>How a sheet's two halves go on the paper.</summary>
public enum SheetLayout
{
    /// <summary>ORIGINAL and DUPLICADO side by side on one landscape page, as the agency's sample.</summary>
    TwoUp,

    /// <summary>Each half on its own page, ORIGINAL first.</summary>
    OnePerPage,
}

/// <summary>
/// The paper rule (spec Decision 19, design D11): two-up when the printable area is landscape, a half is at
/// least <see cref="MinimumHalfWidthMm"/> wide, and one half fits a single page at that width; otherwise one
/// half per page. Type is never scaled down to fit. The 120 mm value is provisional until the manual check M1.
/// </summary>
public static class SheetArrangement
{
    public const double MinimumHalfWidthMm = 120;

    public static SheetLayout For(double printableWidthMm, double printableHeightMm, int halfPageCount) =>
        printableWidthMm > printableHeightMm && printableWidthMm / 2 >= MinimumHalfWidthMm && halfPageCount <= 1
            ? SheetLayout.TwoUp
            : SheetLayout.OnePerPage;

    /// <summary>WPF's device-independent pixels (1/96 inch) in millimetres.</summary>
    public static double Millimetres(double deviceIndependentPixels) => deviceIndependentPixels / 96 * 25.4;
}
