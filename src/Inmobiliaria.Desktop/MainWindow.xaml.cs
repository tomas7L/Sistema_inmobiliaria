using System.Windows;
using Inmobiliaria.Presentation.CashDesk;
using Inmobiliaria.Presentation.GoLive;
using Inmobiliaria.Presentation.Recibos;
using Inmobiliaria.Presentation.Shell;
using Inmobiliaria.Presentation.Voiding;

namespace Inmobiliaria.Desktop;

/// <summary>
/// The main window: who is working and the screens that role is offered, bound to
/// <see cref="ShellViewModel"/>. No logic lives here; the screens arrive with their slices, each given
/// its view-model by the composition root. A screen the role is not offered gets none and is never shown.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(
        ShellViewModel viewModel,
        CashDeskViewModel? cashDesk,
        RecentCollectionsViewModel recentCollections,
        VoidingViewModel? voiding,
        GoLiveViewModel? goLive)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(recentCollections);
        InitializeComponent();

        DataContext = viewModel;
        CashDeskScreen.DataContext = cashDesk;
        RecentCollectionsViewScreen.DataContext = recentCollections;
        VoidingViewScreen.DataContext = voiding;
        GoLiveScreen.DataContext = goLive;
    }

    /// <summary>Selects a screen's navigation entry, when it is offered and available.</summary>
    public void ShowScreen(NavigationTarget target)
    {
        var entry = ((ShellViewModel)DataContext).Navigation.FirstOrDefault(e => e.Target == target && e.IsAvailable);
        if (entry is not null)
        {
            NavigationList.SelectedItem = entry;
        }
    }
}
