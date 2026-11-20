using System.Windows;
using Inmobiliaria.Presentation.Adjustments;
using Inmobiliaria.Presentation.CashDesk;
using Inmobiliaria.Presentation.Contracts;
using Inmobiliaria.Presentation.GoLive;
using Inmobiliaria.Presentation.Indices;
using Inmobiliaria.Presentation.Notices;
using Inmobiliaria.Presentation.Parties;
using Inmobiliaria.Presentation.Recibos;
using Inmobiliaria.Presentation.Shell;
using Inmobiliaria.Presentation.Units;
using Inmobiliaria.Presentation.Users;
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
        GoLiveViewModel? goLive,
        UsersViewModel? users,
        NoticesViewModel? notices,
        ContractsViewModel contracts,
        PartiesViewModel parties,
        UnitsViewModel units,
        IndicesViewModel indices,
        AdjustmentsViewModel adjustments)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(recentCollections);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(parties);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(adjustments);
        InitializeComponent();

        DataContext = viewModel;
        CashDeskScreen.DataContext = cashDesk;
        RecentCollectionsViewScreen.DataContext = recentCollections;
        VoidingViewScreen.DataContext = voiding;
        GoLiveScreen.DataContext = goLive;
        UsersScreen.DataContext = users;
        NoticesScreen.DataContext = notices;
        ContractsScreen.DataContext = contracts;
        PartiesScreen.DataContext = parties;
        UnitsScreen.DataContext = units;
        IndicesScreen.DataContext = indices;
        AdjustmentsScreen.DataContext = adjustments;
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
