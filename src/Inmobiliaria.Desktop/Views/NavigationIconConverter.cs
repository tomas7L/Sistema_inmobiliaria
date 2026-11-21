using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Inmobiliaria.Presentation.Shell;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The icon of a sidebar entry, drawn on the canvas's 24 x 24 grid with the same strokes as the canvas's sidebar. Any other
/// value, as the parameter "logout", gets the Cerrar sesión icon.
/// </summary>
public sealed class NavigationIconConverter : IValueConverter
{
    private const string LogOut = "M10,4 H5 V20 H10 M15,8 L19,12 L15,16 M19,12 H9";

    private static readonly Dictionary<NavigationTarget, Geometry> Icons = new()
    {
        [NavigationTarget.CashDesk] = Parse("M4,6 H20 A2,2 0 0 1 22,8 V17 A2,2 0 0 1 20,19 H4 A2,2 0 0 1 2,17 V8 A2,2 0 0 1 4,6 Z M9,12.5 A3,3 0 1 0 15,12.5 A3,3 0 1 0 9,12.5 Z"),
        [NavigationTarget.RecentCollections] = Parse("M6,3 H18 V21 L15,19 L12,21 L9,19 L6,21 Z M9,8 H15 M9,12 H15"),
        [NavigationTarget.Voiding] = Parse("M3,12 A9,9 0 1 1 21,12 A9,9 0 1 1 3,12 Z M5.6,5.6 L18.4,18.4"),
        [NavigationTarget.Mora] = Parse("M3,12 A9,9 0 1 1 21,12 A9,9 0 1 1 3,12 Z M12,7 V12 L15,14"),
        [NavigationTarget.Notices] = Parse("M5,5 H19 A2,2 0 0 1 21,7 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 V7 A2,2 0 0 1 5,5 Z M3,7 L12,13 L21,7"),
        [NavigationTarget.Contracts] = Parse("M6,3 H15 L19,7 V21 H6 Z M14,3 V8 H19 M9,13 H16 M9,17 H16"),
        [NavigationTarget.Parties] = Parse("M5.5,8 A3.5,3.5 0 1 0 12.5,8 A3.5,3.5 0 1 0 5.5,8 Z M2.5,20 C2.5,16.4 5.4,14 9,14 C12.6,14 15.5,16.4 15.5,20 M16,4.6 A3.5,3.5 0 0 1 16,11.4 M18.5,14.4 C20.4,15.1 21.5,16.9 21.5,20"),
        [NavigationTarget.Units] = Parse("M3,11 L12,3 L21,11 V20 A1,1 0 0 1 20,21 H15 V15 H9 V21 H4 A1,1 0 0 1 3,20 Z"),
        [NavigationTarget.Indices] = Parse("M3,20 H21 M5,16 L10,11 L14,14 L20,6"),
        [NavigationTarget.Adjustments] = Parse("M7,18 V6 M7,6 L4,9 M7,6 L10,9 M17,6 V18 M17,18 L14,15 M17,18 L20,15"),
        [NavigationTarget.GoLive] = Parse("M5,21 V4 M5,4 H16 L14,8 L16,12 H5"),
        [NavigationTarget.Users] = Parse("M6,10 H18 A2,2 0 0 1 20,12 V19 A2,2 0 0 1 18,21 H6 A2,2 0 0 1 4,19 V12 A2,2 0 0 1 6,10 Z M8,10 V7 A4,4 0 0 1 16,7 V10"),
    };

    private static readonly Geometry LogOutIcon = Parse(LogOut);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is NavigationTarget target && Icons.TryGetValue(target, out var icon) ? icon : LogOutIcon;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("An icon is shown, never written back.");

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
