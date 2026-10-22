namespace Inmobiliaria.Presentation.Shell;

/// <summary>The screens the shell can offer. Declared in full from the first slice, so the role rule is proven once.</summary>
public enum NavigationTarget
{
    CashDesk,
    RecentCollections,
    Voiding,
    GoLive,
}

/// <summary>The operator-facing names of the screens, which messages elsewhere point to.</summary>
public static class NavigationTitles
{
    public const string CashDesk = "Caja de cobranza";
    public const string RecentCollections = "Cobros recientes";
    public const string Voiding = "Anular un cobro";
    public const string GoLive = "Puesta en marcha";
}

/// <summary>
/// One line of the shell's navigation. An entry that is offered but cannot open carries the reason in
/// <see cref="UnavailableReason"/>, so the operator sees what to fix instead of a missing screen.
/// </summary>
/// <param name="Target">Which screen this entry opens.</param>
/// <param name="Title">The operator-facing name.</param>
/// <param name="IsAvailable">False when the entry is shown but disabled.</param>
/// <param name="UnavailableReason">Why it is disabled, or null when available.</param>
public sealed record NavigationEntry(
    NavigationTarget Target,
    string Title,
    bool IsAvailable = true,
    string? UnavailableReason = null);
